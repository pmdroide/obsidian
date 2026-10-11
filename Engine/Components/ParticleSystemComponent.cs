using System.Text.Json.Serialization;
using Engine.Entities;
using DirectionalLight = Engine.Entities.DirectionalLight;
using Engine.Logic;
using Engine.Particles;
using Engine.Physics;
using Microsoft.Xna.Framework;

namespace Engine.Components;

/// <summary>
/// A particle system like Unity's: fire, smoke, sparks, magic, explosions. Particles are born from the
/// <see cref="Shape"/> at the gameobject (its position and rotation; scale is ignored), over time and in bursts
/// (<see cref="Emission"/>), and change over their lifetime through the optional modules. They are simulated on
/// the CPU (<see cref="ParticleSimulation"/>) and drawn as billboards by the renderer's particle pass
/// (<see cref="Renderer.RenderModules.ParticleRenderModule"/>).
/// Play On Awake systems start with Play and also play while editing; scripts drive the rest with
/// <see cref="Play"/>, <see cref="Stop"/>, <see cref="Emit(int)"/> and <see cref="EmitAt"/>. The gameobject's
/// own mesh marks the emitter in the editor and is hidden in Play (<see cref="HideMesh"/>).
/// With <see cref="Volumetric"/> on, the system draws as a raymarched volume: its particles' density, or a voxel
/// smoke that floods the room like Counter-Strike 2's (<see cref="CarveTunnel"/>/<see cref="CarveHole"/> shoot through it).
/// </summary>
public sealed class ParticleSystemComponent : GameComponent
{
    public const string TypeId = "particles";
    /// <summary>Edit-mode preview: a finished non-looping system starts again after this pause (seconds).</summary>
    public const float PreviewRestartDelay = 1f;

    private ParticleMainModule _main = new();
    private ParticleEmissionModule _emission = new();
    private ParticleShapeModule _shape = new();
    private ParticleVelocityModule _velocity = new();
    private ParticleLimitVelocityModule _limitVelocity = new();
    private ParticleForceModule _force = new();
    private ParticleExternalForcesModule _externalForces = new();
    private ParticleNoiseModule _noise = new();
    private ParticleColorModule _color = new();
    private ParticleSizeModule _size = new();
    private ParticleRotationModule _rotation = new();
    private ParticleCollisionModule _collision = new();
    private ParticleTextureSheetModule _textureSheet = new();
    private ParticleSubEmittersModule _subEmitters = new();
    private ParticleRendererModule _renderer = new();
    private ParticleVolumetricModule _volumetric = new();

    // Modules are never null (a hand-edited scene may say "Noise": null).
    public ParticleMainModule Main { get => _main; set => _main = value ?? new(); }
    public ParticleEmissionModule Emission { get => _emission; set => _emission = value ?? new(); }
    public ParticleShapeModule Shape { get => _shape; set => _shape = value ?? new(); }
    public ParticleVelocityModule VelocityOverLifetime { get => _velocity; set => _velocity = value ?? new(); }
    public ParticleLimitVelocityModule LimitVelocity { get => _limitVelocity; set => _limitVelocity = value ?? new(); }
    public ParticleForceModule ForceOverLifetime { get => _force; set => _force = value ?? new(); }
    public ParticleExternalForcesModule ExternalForces { get => _externalForces; set => _externalForces = value ?? new(); }
    public ParticleNoiseModule Noise { get => _noise; set => _noise = value ?? new(); }
    public ParticleColorModule ColorOverLifetime { get => _color; set => _color = value ?? new(); }
    public ParticleSizeModule SizeOverLifetime { get => _size; set => _size = value ?? new(); }
    public ParticleRotationModule RotationOverLifetime { get => _rotation; set => _rotation = value ?? new(); }
    public ParticleCollisionModule Collision { get => _collision; set => _collision = value ?? new(); }
    public ParticleTextureSheetModule TextureSheetAnimation { get => _textureSheet; set => _textureSheet = value ?? new(); }
    public ParticleSubEmittersModule SubEmitters { get => _subEmitters; set => _subEmitters = value ?? new(); }
    public ParticleRendererModule Renderer { get => _renderer; set => _renderer = value ?? new(); }
    public ParticleVolumetricModule Volumetric { get => _volumetric; set => _volumetric = value ?? new(); }
    /// <summary>Hides the gameobject's own mesh during Play; while editing it marks (and selects) the emitter.</summary>
    public bool HideMesh { get; set; } = true;

    /// <summary>The live simulation (null until the system first runs).</summary>
    [JsonIgnore] public ParticleSimulation Simulation => _simulation;
    [JsonIgnore] public int ParticleCount => _simulation?.ParticleCount ?? 0;
    [JsonIgnore] public bool IsPlaying => _simulation?.IsPlaying == true;
    [JsonIgnore] public bool IsEmitting => _simulation?.IsEmitting == true;
    [JsonIgnore] public bool IsPaused => _simulation?.IsPaused == true;
    /// <summary>Has particles or will make more.</summary>
    [JsonIgnore] public bool IsAlive => _simulation?.IsAlive == true;
    /// <summary>Seconds into the current cycle.</summary>
    [JsonIgnore] public float Time => _simulation?.Time ?? 0;
    [JsonIgnore] public int CollisionCount => _simulation?.CollisionCount ?? 0;
    /// <summary>The density grid while Volumetric is on and has run (null otherwise).</summary>
    [JsonIgnore] public ParticleVolume Volume => _simulation?.Volume;
    /// <summary>Volumetric and something to draw.</summary>
    [JsonIgnore] public bool HasVolume => Volumetric.Enabled && _simulation?.Volume?.HasDensity == true;
    [JsonIgnore] public BasicEntity Owner => _owner;

    /// <summary>A particle bounced off a collider (Collision module). Raised on the game thread during the step.</summary>
    public event Action<ParticleCollisionEvent> ParticleCollided;

    private BasicEntity _owner;
    private ParticleSimulation _simulation;
    private bool _previewing;
    private float _previewIdle;
    // The simulation knows where the gameobject is (set before the first emission).
    private bool _hasPose;
    private static readonly ParticleStepContext SharedContext = new();

    // ---------------------------------------------------------------------------------------------
    // Script API
    // ---------------------------------------------------------------------------------------------

    /// <summary>Starts emitting from the beginning of the cycle (or resumes after <see cref="Pause"/>).</summary>
    public void Play()
    {
        ParticleSimulation simulation = EnsureSimulation();
        simulation.Play(simulation.IsPlaying || simulation.IsPaused ? null : PrepareContext(_owner));
    }

    /// <summary>Stops emitting; the particles alive live out their lives unless <paramref name="clear"/>.</summary>
    public void Stop(bool clear = false) => _simulation?.Stop(clear);

    /// <summary>Freezes every particle in place until <see cref="Play"/>.</summary>
    public void Pause() => _simulation?.Pause();

    /// <summary>Removes every particle at once.</summary>
    public void Clear() => _simulation?.Clear();

    /// <summary>Clears and plays from the start.</summary>
    public void Restart()
    {
        EnsureSimulation().Stop(clear: true);
        Play();
    }

    /// <summary>Emits <paramref name="count"/> particles from the shape now, whether or not the system plays.</summary>
    public void Emit(int count)
    {
        if (count <= 0) return;
        EnsureSimulation().Emit(count);
    }

    /// <summary>
    /// Emits <paramref name="count"/> particles from the shape centred on a world position, e.g. an impact point,
    /// adding <paramref name="velocity"/> to theirs. Best with World simulation space.
    /// </summary>
    public void EmitAt(Vector3 position, int count, Vector3 velocity = default)
    {
        if (count <= 0) return;
        EnsureSimulation().EmitAt(position, velocity, Vector4.One, count);
    }

    /// <summary>
    /// Volumetric only: cuts a tunnel of <paramref name="radius"/> metres through the volume from
    /// <paramref name="from"/> to <paramref name="to"/>, like a bullet through a Counter-Strike 2 smoke.
    /// It closes again over Refill Time.
    /// </summary>
    public void CarveTunnel(Vector3 from, Vector3 to, float radius = 0.15f) => _simulation?.Volume?.Carve(from, to, radius);

    /// <summary>Volumetric only: blows a ball-shaped hole into the volume (an HE grenade); it refills over Refill Time.</summary>
    public void CarveHole(Vector3 center, float radius = 2.5f) => _simulation?.Volume?.Carve(center, center, radius);

    /// <summary>
    /// Volumetric only: 0..1, how much of the view from <paramref name="from"/> to <paramref name="to"/> this volume
    /// hides (holes included, detail noise not). 0 without a volume.
    /// </summary>
    public float Opacity(Vector3 from, Vector3 to)
    {
        ParticleVolume volume = Volumetric.Enabled ? _simulation?.Volume : null;
        if (volume == null || !volume.HasDensity) return 0;
        return 1 - volume.Transmittance(from, to, Math.Max(Volumetric.Density, 0), Volumetric.RefillTime);
    }

    /// <summary>
    /// 0..1, how much the volumetric systems of the active scene (or <paramref name="entities"/>) together hide
    /// the view between two points: what an NPC's sight or a script asks before "seeing" through smoke.
    /// </summary>
    public static float VolumeOpacity(Vector3 from, Vector3 to, IEnumerable<BasicEntity> entities = null)
    {
        float through = 1;
        foreach (ParticleSystemComponent system in Volumes(entities))
            through *= 1 - system.Opacity(from, to);
        return 1 - through;
    }

    /// <summary>A bullet through every volumetric system of the active scene (or <paramref name="entities"/>): see <see cref="CarveTunnel"/>.</summary>
    public static void CarveTunnels(Vector3 from, Vector3 to, float radius = 0.15f, IEnumerable<BasicEntity> entities = null)
    {
        foreach (ParticleSystemComponent system in Volumes(entities)) system.CarveTunnel(from, to, radius);
    }

    /// <summary>A blast into every volumetric system of the active scene (or <paramref name="entities"/>): see <see cref="CarveHole"/>.</summary>
    public static void CarveHoles(Vector3 center, float radius = 2.5f, IEnumerable<BasicEntity> entities = null)
    {
        foreach (ParticleSystemComponent system in Volumes(entities)) system.CarveHole(center, radius);
    }

    private static IEnumerable<ParticleSystemComponent> Volumes(IEnumerable<BasicEntity> entities)
    {
        entities ??= GameFlow.SceneLogic?.ActiveScene?.BasicEntities;
        if (entities == null) yield break;
        foreach (BasicEntity entity in entities)
        {
            if (entity == null || !entity.IsEnabled) continue;
            foreach (GameComponent component in entity.Components)
                if (component is ParticleSystemComponent { Enabled: true, HasVolume: true } system) yield return system;
        }
    }

    /// <summary>Fast-forwards the simulation by <paramref name="seconds"/> (in 1/30 s steps).</summary>
    public void Simulate(float seconds)
    {
        if (!(seconds > 0)) return;
        ParticleSimulation simulation = EnsureSimulation();
        simulation.Prewarm(Math.Min(seconds, 600), PrepareContext(_owner));
    }

    // ---------------------------------------------------------------------------------------------
    // Component hooks
    // ---------------------------------------------------------------------------------------------

    public override void OnAdded(BasicEntity owner)
    {
        _owner = owner;
        base.OnAdded(owner);
    }

    public override void OnStart(BasicEntity owner)
    {
        OnStop();
        _owner = owner; // null on the camera, which has no position
        if (owner == null) return;
        SetMeshHidden(HideMesh);
        if (Main.PlayOnAwake) Play();
    }

    public override void OnUpdate(BasicEntity owner, GameTime time)
    {
        if (owner == null) return;
        _owner = owner;
        Step((float)time.ElapsedGameTime.TotalSeconds);
    }

    public override void OnChanged(BasicEntity owner)
    {
        if (owner != null) _owner = owner;
        base.OnChanged(owner);
        // In Play the mesh follows Hide Mesh; while editing it always shows.
        if (Enabled && owner?.IsEnabled == true && _simulation != null && !_previewing) SetMeshHidden(HideMesh);
    }

    public override void OnRemoved(BasicEntity owner)
    {
        base.OnRemoved(owner);
        _owner = null;
    }

    public override void OnStop()
    {
        _simulation?.Reset();
        _hasPose = false;
        _previewing = false;
        _previewIdle = 0;
        SetMeshHidden(false);
    }

    /// <summary>
    /// Runs the systems of <paramref name="entities"/> while editing (Anvil's viewport), so effects show without
    /// pressing Play. Play On Awake systems play and loop; a finished one-shot starts again after a pause.
    /// </summary>
    public static void UpdatePreviews(IEnumerable<BasicEntity> entities, GameTime time)
    {
        if (entities == null) return;
        float dt = (float)time.ElapsedGameTime.TotalSeconds;
        foreach (BasicEntity entity in entities)
            foreach (GameComponent component in entity.Components)
                if (component is ParticleSystemComponent particles) particles.UpdatePreview(entity, dt);
    }

    internal void UpdatePreview(BasicEntity owner, float dt)
    {
        if (!Enabled || !owner.IsEnabled)
        {
            if (_previewing || _simulation?.ParticleCount > 0) OnStop();
            return;
        }
        _owner = owner;
        if (!_previewing)
        {
            _previewing = true;
            _previewIdle = 0;
            EnsureSimulation().Reset();
            if (Main.PlayOnAwake) Play();
        }
        else if (Main.PlayOnAwake && !_simulation.IsAlive)
        {
            _previewIdle += dt;
            if (_previewIdle >= PreviewRestartDelay)
            {
                _previewIdle = 0;
                Restart();
            }
        }
        Step(dt);
    }

    // ---------------------------------------------------------------------------------------------
    // Internals
    // ---------------------------------------------------------------------------------------------

    private void Step(float dt)
    {
        if (_owner == null) return;
        ParticleSimulation simulation = EnsureSimulation();
        simulation.Step(dt, WorldOf(_owner), PrepareContext(_owner));
    }

    private ParticleSimulation EnsureSimulation()
    {
        if (_simulation == null)
        {
            _simulation = new ParticleSimulation(this);
            _simulation.Collided += hit => ParticleCollided?.Invoke(hit);
        }
        if (_owner != null && !_hasPose)
        {
            _simulation.SetPose(WorldOf(_owner));
            _hasPose = true;
        }
        return _simulation;
    }

    /// <summary>This system as another system's sub-emitter target (null when disabled).</summary>
    internal ParticleSimulation AsSubEmitter(BasicEntity owner)
    {
        if (!Enabled || owner == null || !owner.IsEnabled) return null;
        _owner = owner;
        return EnsureSimulation();
    }

    // One context for all systems: they step one after another on the game thread.
    private static ParticleStepContext PrepareContext(BasicEntity owner)
    {
        ParticleStepContext context = SharedContext;
        ScenePhysics physics = owner?.PhysicsScene ?? ScenePhysics.Current;
        context.Gravity = physics?.Gravity ?? new Vector3(0, 0, -9.81f);
        context.Wind = WeatherProfile.Wind(GameFlow.SceneLogic?.ActiveScene?.Environment);
        context.Raycast = physics == null ? null : RaycastFor(physics, owner);
        context.RaycastStatic = physics == null ? null : StaticRaycastFor(physics);
        DirectionalLight sun = GameFlow.SceneLogic?.ActiveScene?.DirectionalLights?.FirstOrDefault(l => l.IsEnabled && l.Intensity > 0);
        context.SunDirection = sun != null && sun.Direction.LengthSquared() > 1e-8f ? -Vector3.Normalize(sun.Direction) : null;
        context.FindSubEmitter = FindSubEmitter;
        return context;
    }

    private static ParticleRaycast RaycastFor(ScenePhysics physics, BasicEntity owner) =>
        (Vector3 origin, Vector3 direction, float maxDistance, out Vector3 point, out Vector3 normal, out BasicEntity hit) =>
        {
            bool found = physics.Raycast(origin, direction, maxDistance, out RaycastHit result, owner);
            point = result.Point;
            normal = result.Normal;
            hit = result.GameObject;
            return found;
        };

    private static ParticleRaycast StaticRaycastFor(ScenePhysics physics) =>
        (Vector3 origin, Vector3 direction, float maxDistance, out Vector3 point, out Vector3 normal, out BasicEntity hit) =>
        {
            bool found = physics.RaycastStatic(origin, direction, maxDistance, out RaycastHit result);
            point = result.Point;
            normal = result.Normal;
            hit = result.GameObject;
            return found;
        };

    private static ParticleSimulation FindSubEmitter(string name)
    {
        List<BasicEntity> entities = GameFlow.SceneLogic?.ActiveScene?.BasicEntities;
        if (entities == null) return null;
        foreach (BasicEntity entity in entities)
        {
            if (!string.Equals(entity.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
            if (entity.GetComponent<ParticleSystemComponent>() is { } particles) return particles.AsSubEmitter(entity);
        }
        return null;
    }

    private void SetMeshHidden(bool hidden)
    {
        if (_owner == null || _owner.WorldTransform.Hidden == hidden) return;
        _owner.WorldTransform.Hidden = hidden;
        _owner.WorldTransform.HasChanged = true;
    }

    /// <summary>The emitter's pose: the gameobject's rotation and position (particles ignore its scale).</summary>
    public static Matrix WorldOf(BasicEntity owner) => owner.RotationMatrix * Matrix.CreateTranslation(owner.Position);
}
