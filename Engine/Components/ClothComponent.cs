using System.Text.Json.Serialization;
using Engine.Editor;
using Engine.Entities;
using Engine.Logic;
using Engine.Physics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Components;

/// <summary>
/// Turns the gameobject's mesh into cloth during Play (the Cloth model is a subdivided, double-sided plane made
/// for it). Its vertices become particles that fall, flutter in the scene's wind and collide with every Physics
/// collider around them: the player walks through a curtain, a sheet drapes over a crate. <see cref="Pin"/>ned
/// particles follow the gameobject. The cloth is pushed by bodies but doesn't push back. Edit mode shows the
/// mesh at rest; Stop puts it back.
/// </summary>
public sealed class ClothComponent : GameComponent
{
    public const string TypeId = "cloth";
    public const float MinMass = 0.01f;

    /// <summary>Which particles hold still and move with the gameobject.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ClothPinMode Pin { get; set; } = ClothPinMode.TopEdge;
    /// <summary>Whole cloth, in kilograms.</summary>
    public float Mass { get; set; } = 1f;
    /// <summary>How well it keeps its size: 1 doesn't stretch, 0 is elastic.</summary>
    public float Stiffness { get; set; } = 0.95f;
    /// <summary>How much it resists folding: 0 is silk, 1 is cardboard.</summary>
    public float Bend { get; set; } = 0.3f;
    /// <summary>Share of its speed lost per second, besides air drag (0..1).</summary>
    public float Damping { get; set; } = 0.1f;
    /// <summary>Grip on colliders: 0 slides off everything.</summary>
    public float Friction { get; set; } = 0.5f;
    /// <summary>Distance (m) kept from colliders.</summary>
    public float Thickness { get; set; } = 0.03f;
    /// <summary>Multiplies the scene's wind (Environment > Wind Speed/Direction); 0 is still air.</summary>
    public float Wind { get; set; } = 1f;
    /// <summary>Collide with Physics colliders (bodies, statics, ragdolls; never triggers).</summary>
    public bool Collisions { get; set; } = true;

    /// <summary>The simulation during Play, or null.</summary>
    [JsonIgnore] public Cloth Cloth => _cloth;
    /// <summary>Gameobjects the cloth touched in the last frame (empty outside Play).</summary>
    [JsonIgnore] public IReadOnlyCollection<BasicEntity> Touching => (IReadOnlyCollection<BasicEntity>)_cloth?.Touching ?? Array.Empty<BasicEntity>();

    public float ClampedMass => float.IsFinite(Mass) ? Math.Max(Mass, MinMass) : 1f;
    public float ClampedStiffness => Clamp01(Stiffness, 0.95f);
    public float ClampedBend => Clamp01(Bend, 0.3f);
    public float ClampedDamping => Clamp01(Damping, 0.1f);
    public float ClampedFriction => float.IsFinite(Friction) ? Math.Clamp(Friction, 0, 2) : 0.5f;
    public float ClampedThickness => float.IsFinite(Thickness) ? Math.Clamp(Thickness, 0, 0.5f) : 0.03f;
    public float ClampedWind => float.IsFinite(Wind) ? Math.Clamp(Wind, 0, 10) : 1f;

    /// <summary>Topology for gameobjects without a loaded model (GPU-free checks in Tests/Components).</summary>
    internal static Func<BasicEntity, ClothTopology> FallbackTopology;

    private BasicEntity _owner;
    private Cloth _cloth;
    private ClothMeshInstance _mesh;
    private ScenePhysics _scene;
    private Vector3 _builtScale;
    private bool _failed;
    private bool _warned;

    public override void OnStart(BasicEntity owner)
    {
        OnStop();
        _owner = owner; // null on the camera, which has no mesh
    }

    public override void OnUpdate(BasicEntity owner, GameTime time)
    {
        if (owner == null) return;
        if (owner != _owner)
        {
            OnStop();
            _owner = owner;
        }
        // Rest lengths are in world units, so a new scale needs a new cloth.
        if (_cloth != null && owner.Scale != _builtScale) RemoveCloth();
        if (_cloth == null && (_failed || !Build())) return;

        Matrix world = WorldOf(owner);
        float dt = (float)time.ElapsedGameTime.TotalSeconds;
        ApplySettings();
        if (Collisions && _scene != null)
        {
            // Everything the particles can reach this frame.
            BoundingBox bounds = _cloth.Bounds;
            var reach = new Vector3(ClampedThickness + _cloth.TopSpeed * Math.Min(dt, 1f / 30f) * 2 + 0.05f);
            _scene.CollectClothColliders(bounds.Min - reach, bounds.Max + reach, _cloth.Colliders, owner);
        }
        else _cloth.Colliders.Clear();

        if (!_cloth.Step(dt, world) && !_warned)
        {
            _warned = true;
            EditorBridge.Log($"Cloth: '{owner.Name}' became unstable and was reset; try a lower Mass or Stiffness.");
        }
        if (_mesh == null) return;
        _mesh.Update(_cloth.Positions, world);
        owner.WorldTransform.Cloth = _mesh;
        // Vertices moved: redo culling and redraw shadow maps this frame.
        owner.WorldTransform.HasChanged = true;
    }

    /// <summary>Instant change of momentum (kg*m/s) around a world-space point, e.g. a hit or a gust. Only during Play.</summary>
    public void AddImpulse(Vector3 impulse, Vector3 point, float radius = 0.5f) => _cloth?.AddImpulse(impulse, point, radius);

    /// <summary>Puts the cloth back at rest on the gameobject (during Play).</summary>
    public void ResetCloth()
    {
        if (_cloth != null && _owner != null) _cloth.Reset(WorldOf(_owner));
    }

    public override void OnChanged(BasicEntity owner)
    {
        base.OnChanged(owner);
        _failed = false;
        if (_cloth == null) return;
        if (_cloth.Pin != Pin) _cloth.SetPin(Pin);
        if (_cloth.Mass != ClampedMass) _cloth.SetMass(ClampedMass);
    }

    public override void OnStop()
    {
        RemoveCloth();
        _failed = false;
        _warned = false;
    }

    private bool Build()
    {
        ClothTopology topology;
        if (_owner.Model != null)
        {
            GraphicsDevice device = FindDevice(_owner.Model);
            if (device == null) return Fail("its model has no vertex buffers");
            _mesh = new ClothMeshInstance(device, _owner.Model);
            if (!_mesh.IsValid)
            {
                _mesh.Dispose();
                _mesh = null;
                return Fail("its model has no triangles");
            }
            topology = _mesh.Topology;
        }
        else
        {
            topology = FallbackTopology?.Invoke(_owner);
            if (topology == null || topology.ParticleCount == 0) return Fail("it has no model");
        }

        _scene = _owner.PhysicsScene ?? ScenePhysics.Current;
        _builtScale = _owner.Scale;
        _cloth = new Cloth(topology, WorldOf(_owner), Pin, ClampedMass);
        ApplySettings();
        return true;
    }

    private bool Fail(string reason)
    {
        EditorBridge.Log($"Cloth: '{_owner.Name}' can't be cloth: {reason}.");
        _failed = true; // an inspector edit retries
        return false;
    }

    private void ApplySettings()
    {
        _cloth.Stiffness = ClampedStiffness;
        _cloth.Bend = ClampedBend;
        _cloth.Damping = ClampedDamping;
        _cloth.Friction = ClampedFriction;
        _cloth.Thickness = ClampedThickness;
        _cloth.Gravity = _scene?.Gravity ?? new Vector3(0, 0, -9.81f);
        Vector2 wind = WeatherProfile.Wind(GameFlow.SceneLogic?.ActiveScene?.Environment) * ClampedWind;
        _cloth.Wind = new Vector3(wind, 0);
    }

    private void RemoveCloth()
    {
        if (_owner != null && _mesh != null && _owner.WorldTransform.Cloth == _mesh)
        {
            _owner.WorldTransform.Cloth = null;
            _owner.WorldTransform.HasChanged = true;
        }
        _mesh?.Dispose();
        _mesh = null;
        _cloth = null;
        _scene = null;
    }

    private static Matrix WorldOf(BasicEntity owner) =>
        Matrix.CreateScale(owner.Scale) * owner.RotationMatrix * Matrix.CreateTranslation(owner.Position);

    private static float Clamp01(float value, float fallback) => float.IsFinite(value) ? Math.Clamp(value, 0, 1) : fallback;

    private static GraphicsDevice FindDevice(Model model)
    {
        foreach (ModelMesh mesh in model.Meshes)
            foreach (ModelMeshPart part in mesh.MeshParts)
                if (part.VertexBuffer != null) return part.VertexBuffer.GraphicsDevice;
        return null;
    }
}
