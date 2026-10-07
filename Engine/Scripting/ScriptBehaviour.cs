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

    // Event hooks run on the game thread right after the physics step (after every Update that frame).
    // They need a Physics component on this gameobject. Triggers: PhysicsComponent.IsTrigger.

    /// <summary>This gameobject started touching another collider.</summary>
    public virtual void OnCollisionEnter(Collision collision) { }
    /// <summary>Every physics step while the two keep touching.</summary>
    public virtual void OnCollisionStay(Collision collision) { }
    /// <summary>The two stopped touching (or the other was removed). The collision holds the last contact.</summary>
    public virtual void OnCollisionExit(Collision collision) { }
    /// <summary>A gameobject started overlapping this trigger, or this gameobject entered a trigger (<paramref name="other"/>).</summary>
    public virtual void OnTriggerEnter(BasicEntity other) { }
    /// <summary>Every physics step while the overlap lasts.</summary>
    public virtual void OnTriggerStay(BasicEntity other) { }
    /// <summary>The overlap ended (or the other was removed).</summary>
    public virtual void OnTriggerExit(BasicEntity other) { }
    /// <summary>Another script used this gameobject's Interactable component (<see cref="Interact(RaycastHit)"/>).</summary>
    public virtual void OnInteract(Interaction interaction) { }

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

    /// <summary>Runs <see cref="Stop"/>, then releases sounds and gameobjects this script started or spawned.</summary>
    internal void Shutdown()
    {
        try { Stop(); }
        finally
        {
            StopAllSounds();
            DestroyAllSpawned();
        }
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
    /// Physics component (and not the Water role) can be hit; triggers only with <paramref name="includeTriggers"/>.
    /// </summary>
    public bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, out RaycastHit hit, bool includeTriggers = false)
    {
        hit = default;
        var scene = PhysicsScene;
        return scene != null && scene.Raycast(origin, direction, maxDistance, out hit, GameObject, includeTriggers);
    }

    /// <summary>Raycast without hit details.</summary>
    public bool Raycast(Vector3 origin, Vector3 direction, float maxDistance) =>
        Raycast(origin, direction, maxDistance, out _);

    /// <summary>Raycast along a <see cref="Ray"/>, e.g. <see cref="CameraRay"/> or <see cref="MouseRay"/>.</summary>
    public bool Raycast(Ray ray, float maxDistance, out RaycastHit hit, bool includeTriggers = false) =>
        Raycast(ray.Position, ray.Direction, maxDistance, out hit, includeTriggers);

    /// <summary>True while this gameobject touches or overlaps <paramref name="other"/> (as of the last physics step).</summary>
    public bool IsTouching(BasicEntity other) => GameObject != null && PhysicsScene?.IsTouching(GameObject, other) == true;

    // The same helpers for another gameobject's Dynamic body (no-ops without one).

    public Vector3 GetVelocity(BasicEntity target) =>
        target?.PhysicsScene?.TryGetVelocity(target, out var linear, out _) == true ? linear : Vector3.Zero;

    public void SetVelocity(BasicEntity target, Vector3 velocity)
    {
        var scene = target?.PhysicsScene;
        if (scene != null && scene.TryGetVelocity(target, out _, out var angular)) scene.SetVelocity(target, velocity, angular);
    }

    public void SetAngularVelocity(BasicEntity target, Vector3 angularVelocity)
    {
        var scene = target?.PhysicsScene;
        if (scene != null && scene.TryGetVelocity(target, out var linear, out _)) scene.SetVelocity(target, linear, angularVelocity);
    }

    /// <summary>Instant kick on another gameobject, e.g. a launch pad or a shove.</summary>
    public void AddImpulse(BasicEntity target, Vector3 impulse) => target?.PhysicsScene?.ApplyImpulse(target, impulse);

    /// <summary>Instant kick on another gameobject at a world-space point, which also makes it spin.</summary>
    public void AddImpulseAtPosition(BasicEntity target, Vector3 impulse, Vector3 worldPoint) =>
        target?.PhysicsScene?.ApplyImpulse(target, impulse, worldPoint);

    private ScenePhysics PhysicsScene => GameObject?.PhysicsScene ?? ScenePhysics.Current;

    ////////////////////////////////////////////////////////////////////////////////
    //  CAMERA RAYS & INTERACTION (Interactable component)
    ////////////////////////////////////////////////////////////////////////////////

    /// <summary>The scene's main camera (the one Play renders), whichever object this script runs on.</summary>
    public Camera MainCamera => Camera ?? Logic.GameFlow.SceneLogic?.ActiveScene?.MainCamera;

    /// <summary>From the main camera through the centre of the screen (the crosshair).</summary>
    public Ray CameraRay
    {
        get
        {
            Camera camera = MainCamera;
            return camera == null ? new Ray(Position, Forward) : new Ray(camera.Position, SafeNormalize(camera.Forward, Vector3.UnitY));
        }
    }

    /// <summary>From the main camera through the mouse cursor.</summary>
    public Ray MouseRay => ScreenPointToRay(Logic.GameInput.MousePosition);

    /// <summary>From the main camera through a pixel of the game view (0,0 = top left).</summary>
    public Ray ScreenPointToRay(Point pixel)
    {
        Camera camera = MainCamera;
        if (camera == null) return CameraRay;
        int width = Math.Max(1, GameSettings.g_screenwidth), height = Math.Max(1, GameSettings.g_screenheight);
        // The renderer's projection (Renderer.UpdateViewProjection): near plane 1, far plane g_farplane.
        Matrix view = Matrix.CreateLookAt(camera.Position, camera.Lookat, camera.Up);
        Matrix projection = Matrix.CreatePerspectiveFieldOfView(camera.FieldOfView, width / (float)height, 1f,
            Math.Max(GameSettings.g_farplane, 2f));
        var viewport = new Microsoft.Xna.Framework.Graphics.Viewport(0, 0, width, height);
        Vector3 near = viewport.Unproject(new Vector3(pixel.X, pixel.Y, 0f), projection, view, Matrix.Identity);
        Vector3 far = viewport.Unproject(new Vector3(pixel.X, pixel.Y, 1f), projection, view, Matrix.Identity);
        return new Ray(camera.Position, SafeNormalize(far - near, camera.Forward));
    }

    /// <summary>
    /// The interactable the ray points at, or null. The first collider hit (ignoring this gameobject
    /// and triggers) must have an enabled Interactable component and be within its Range.
    /// </summary>
    public InteractableComponent FindInteractable(Ray ray, float maxDistance, out RaycastHit hit) =>
        InteractableComponent.Find(PhysicsScene, ray.Position, ray.Direction, maxDistance, out hit, GameObject);

    /// <summary>Uses what the ray hit: runs OnInteract on its scripts. False when it has no enabled Interactable.</summary>
    public bool Interact(RaycastHit hit) =>
        InteractableComponent.Use(hit.GameObject, new Interaction { Interactor = GameObject, Hit = hit });

    /// <summary>Uses a gameobject directly (no ray), e.g. from a trigger or a menu.</summary>
    public bool Interact(BasicEntity target) =>
        InteractableComponent.Use(target, new Interaction { Interactor = GameObject, Hit = new RaycastHit { GameObject = target } });

    /// <summary>First gameobject in the active scene with this name, or null.</summary>
    public BasicEntity FindGameObject(string name) =>
        Logic.GameFlow.SceneLogic?.BasicEntities.FirstOrDefault(e => e.Name == name);

    ////////////////////////////////////////////////////////////////////////////////
    //  SPAWNING (gameobjects that exist only while this script runs)
    ////////////////////////////////////////////////////////////////////////////////

    private readonly List<BasicEntity> _spawned = new();

    /// <summary>
    /// Adds a gameobject from a model key ("Capsule", "Cube", "IsoSphere"). It is never saved and is
    /// removed when this script stops. Add components with <c>AddComponent</c>. Null for an unknown key.
    /// </summary>
    public BasicEntity Spawn(string modelKey, Vector3 position, string name = null)
    {
        BasicEntity entity = Logic.GameFlow.SceneLogic?.SpawnRuntimeEntity(modelKey, position, name);
        if (entity != null) _spawned.Add(entity);
        return entity;
    }

    /// <summary>Removes a gameobject this script spawned (other gameobjects are left alone).</summary>
    public void Destroy(BasicEntity entity)
    {
        if (entity == null || !_spawned.Remove(entity)) return;
        var sceneLogic = Logic.GameFlow.SceneLogic;
        // After a scene switch the old scene is gone, and a new object may reuse the ID.
        if (sceneLogic != null && sceneLogic.BasicEntities.Contains(entity)) sceneLogic.EditorDelete(entity.Id);
    }

    /// <summary>Removes every gameobject this script spawned (also happens automatically when it stops).</summary>
    public void DestroyAllSpawned()
    {
        foreach (var entity in _spawned.ToArray()) Destroy(entity);
    }

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
