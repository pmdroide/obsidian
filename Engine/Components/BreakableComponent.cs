using System.Text.Json.Serialization;
using Engine.Animation;
using Engine.Entities;
using Engine.Logic;
using Engine.Particles;
using Engine.Physics;
using Microsoft.Xna.Framework;

namespace Engine.Components;

/// <summary>
/// Makes a gameobject break into pieces during Play: a vase knocked off a table, a pot that is shot, a zombie's
/// head. When Play starts its model is fractured on a worker thread into <see cref="Pieces"/> Voronoi pieces
/// (<see cref="MeshFracture"/>; closed models give closed pieces whose cut faces show <see cref="InteriorColor"/>).
/// A hard enough hit (<see cref="ImpactSpeed"/>, <see cref="ImpactMomentum"/>; a falling Dynamic gameobject hitting
/// the floor counts) or a script's <see cref="Break(Vector3, Vector3, float)"/> hides it, removes its collider and
/// throws the pieces out from the hit as physics bodies that fade after <see cref="DebrisLifetime"/>, with a burst of
/// dust or blood (<see cref="Effect"/>). With <see cref="FollowObject"/> it rides on a gameobject or one of its bones
/// (a head on a zombie), shrinks that bone away when it breaks (<see cref="CollapseBone"/>) and can make the body go
/// limp (<see cref="RagdollOnBreak"/>). Give it a Physics component so bullets and bodies can hit it. Stop puts it back.
/// </summary>
public sealed class BreakableComponent : GameComponent
{
    public const string TypeId = "breakable";

    /// <summary>How many pieces it breaks into (2..64).</summary>
    public int Pieces { get; set; } = 12;
    /// <summary>Speed (m/s) a contact must have to break it; 0 = only scripts break it.</summary>
    public float ImpactSpeed { get; set; } = 4f;
    /// <summary>Momentum (kg·m/s) of the contact: the lighter body's share of the masses x speed (its own mass against the floor).</summary>
    public float ImpactMomentum { get; set; } = 2f;
    /// <summary>Speed (m/s) the pieces fly apart with; hits add some of their own.</summary>
    public float BreakForce { get; set; } = 2.5f;
    /// <summary>Seconds the pieces last before they shrink away. The scene keeps at most 200.</summary>
    public float DebrisLifetime { get; set; } = 10f;
    /// <summary>Kilograms per cubic metre of the pieces when the gameobject isn't Dynamic (a Dynamic one shares its mass out).</summary>
    public float Density { get; set; } = 1500f;
    /// <summary>Colour of the faces the break cuts (clay, plaster, flesh). See-through materials stay themselves inside.</summary>
    [JsonConverter(typeof(ParticleColorJsonConverter))]
    public Color InteriorColor { get; set; } = new Color(196, 168, 140);
    /// <summary>What it throws out besides the pieces.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public BreakEffect Effect { get; set; } = BreakEffect.Dust;
    /// <summary>Colour of the dust or blood.</summary>
    [JsonConverter(typeof(ParticleColorJsonConverter))]
    public Color EffectColor { get; set; } = DustColor;
    /// <summary>Scales how many particles the effect throws (0..5).</summary>
    public float EffectAmount { get; set; } = 1f;
    /// <summary>Gameobject (by name) to ride on during Play, keeping the offset it has in the editor; empty = stays put.</summary>
    public string FollowObject { get; set; } = "";
    /// <summary>Bone of <see cref="FollowObject"/> to ride on (e.g. <c>mixamorig:Head</c>); empty = the gameobject itself.</summary>
    public string FollowBone { get; set; } = "";
    /// <summary>When it breaks, the followed bone (and the bones under it) shrink away on the followed gameobject.</summary>
    public bool CollapseBone { get; set; } = true;
    /// <summary>When it breaks, the followed gameobject's Ragdoll goes limp and is pushed along the hit.</summary>
    public bool RagdollOnBreak { get; set; } = true;

    /// <summary>Default <see cref="EffectColor"/> of <see cref="BreakEffect.Dust"/>.</summary>
    public static readonly Color DustColor = new(176, 160, 140);
    /// <summary>Default <see cref="EffectColor"/> of <see cref="BreakEffect.Blood"/>.</summary>
    public static readonly Color BloodColor = new(120, 8, 10);

    public int ClampedPieces => Math.Clamp(Pieces, 2, 64);
    public float ClampedImpactSpeed => float.IsFinite(ImpactSpeed) ? Math.Max(ImpactSpeed, 0) : 4f;
    public float ClampedImpactMomentum => float.IsFinite(ImpactMomentum) ? Math.Max(ImpactMomentum, 0) : 2f;
    public float ClampedBreakForce => float.IsFinite(BreakForce) ? Math.Clamp(BreakForce, 0, 40) : 2.5f;
    public float ClampedDebrisLifetime => float.IsFinite(DebrisLifetime) ? Math.Clamp(DebrisLifetime, 0.1f, 600f) : 10f;
    public float ClampedDensity => float.IsFinite(Density) ? Math.Clamp(Density, 1f, 20000f) : 1500f;
    public float ClampedEffectAmount => float.IsFinite(EffectAmount) ? Math.Clamp(EffectAmount, 0, 5) : 1f;

    /// <summary>It has broken (during Play).</summary>
    [JsonIgnore] public bool IsBroken => _fracture is { IsBroken: true, IsDisposed: false };
    /// <summary>The pieces are computed; a break before that waits for them.</summary>
    [JsonIgnore] public bool IsReady => _fracture is { IsReady: true, IsDisposed: false };
    /// <summary>Its pieces still in the scene.</summary>
    [JsonIgnore] public int PieceCount => _fracture?.PieceCount ?? 0;
    /// <summary>The simulation during Play, or null.</summary>
    [JsonIgnore] public FractureShatter Fracture => _fracture;
    /// <summary>The gameobject it rides on during Play (<see cref="FollowObject"/>), or null.</summary>
    [JsonIgnore] public BasicEntity Followed => _target;

    /// <summary>After it broke: world point of the hit and pieces thrown.</summary>
    public event Action<Vector3, int> Broken;

    private BasicEntity _owner;
    private FractureShatter _fracture;
    private ScenePhysics _scene;
    private Vector3 _lastPosition, _velocity;
    // Following: the gameobject, its bone (-1 = the gameobject itself) and the owner's pose relative to it.
    private BasicEntity _target;
    private int _bone = -1;
    private Matrix _offset;
    private BasicEntity _collapsedOn;
    private string _collapsedBone;

    public override void OnStart(BasicEntity owner)
    {
        OnStop();
        _owner = owner; // null on the camera, which has nothing to break
        if (owner == null) return;
        _lastPosition = owner.Position;
        _velocity = Vector3.Zero;
        Attach();
        Build();
    }

    public override void OnUpdate(BasicEntity owner, GameTime time)
    {
        if (owner == null) return;
        if (owner != _owner)
        {
            OnStop();
            _owner = owner;
            Attach();
        }
        float dt = (float)time.ElapsedGameTime.TotalSeconds;
        if (_fracture == null || _fracture.IsDisposed) Build();
        if (_fracture == null) return;
        ApplySettings();
        if (!_fracture.IsBroken)
        {
            Follow();
            _fracture.Prepare(ClampedPieces);
        }
        if (dt > 0)
        {
            _velocity = (owner.Position - _lastPosition) / dt;
            _lastPosition = owner.Position;
        }
    }

    /// <summary>
    /// Breaks it at a world point (a bullet's hit, a blast's centre), the pieces flying out from there and along
    /// <paramref name="direction"/> at about <paramref name="force"/> m/s (negative = <see cref="BreakForce"/>).
    /// Only during Play; returns false outside Play or when it is already broken.
    /// </summary>
    public bool Break(Vector3 point, Vector3 direction, float force = -1f)
    {
        if (!Enabled || _owner == null) return false;
        if (_fracture == null || _fracture.IsDisposed) Build();
        if (_fracture == null || _fracture.IsBroken) return false;
        ApplySettings();
        BreakNow(point, direction, force < 0 ? ClampedBreakForce : force);
        return true;
    }

    /// <summary>Breaks it from the middle (no direction).</summary>
    public bool Break() => Break(Center(), Vector3.Zero);

    /// <summary>Breaks it at a ray's hit (e.g. <c>Raycast(CameraRay, ...)</c>) along the ray.</summary>
    public bool Break(RaycastHit hit, Vector3 direction, float force = -1f) => Break(hit.Point, direction, force);

    /// <summary>Puts it back whole (during Play), removing its pieces and showing a collapsed bone again.</summary>
    public void Restore()
    {
        if (_owner == null) return;
        Remove();
        Uncollapse();
        Build();
    }

    public override void OnChanged(BasicEntity owner)
    {
        base.OnChanged(owner);
        if (_fracture != null && !Enabled) Remove();
    }

    public override void OnStop()
    {
        Remove();
        Uncollapse();
        _owner = null;
        _target = null;
        _bone = -1;
    }

    private void Build()
    {
        if (!Enabled || _owner == null) return;
        _scene = _owner.PhysicsScene ?? ScenePhysics.Current;
        if (_scene == null) return;
        _fracture = _scene.CreateFracture(_owner, GameFlow.SceneLogic?.MeshMaterialLibrary);
        _fracture.Hit = OnHit;
        ApplySettings();
        _fracture.Prepare(ClampedPieces);
    }

    private void Remove()
    {
        if (_fracture == null) return;
        _fracture.Hit = null;
        if (_scene != null) _scene.RemoveFracture(_fracture);
        else _fracture.Dispose();
        _fracture = null;
        _scene = null;
    }

    private void ApplySettings()
    {
        _fracture.DebrisLifetime = ClampedDebrisLifetime;
        _fracture.Density = ClampedDensity;
        _fracture.InteriorColor = InteriorColor;
    }

    private void OnHit(PhysicsContact contact, BasicEntity other, float otherMass)
    {
        float speed = ClampedImpactSpeed;
        if (!Enabled || !(speed > 0) || contact.IsTrigger || contact.ImpactSpeed < speed) return;
        if (_fracture == null || _fracture.IsBroken) return;
        // The share of the masses that takes the blow: against the immovable floor its own, between two bodies the
        // reduced mass, so a pebble hardly counts against a vase.
        float own = _owner.AttachedPhysicsType == PhysicsBodyType.Dynamic ? _owner.AttachedMass : 0;
        float mass = own > 0 && otherMass > 0 ? own * otherMass / (own + otherMass) : Math.Max(own, otherMass);
        if (mass * contact.ImpactSpeed < ClampedImpactMomentum) return;
        // Seen from it, the normal points from the other body into it: the way a thrown body travels. Something that
        // falls onto the floor just bursts apart.
        Vector3 direction = otherMass > 0 ? contact.Normal : Vector3.Zero;
        float force = ClampedBreakForce + contact.ImpactSpeed * 0.2f;
        Vector3 point = contact.Point;
        _fracture.Queue(() =>
        {
            if (_fracture != null && !_fracture.IsBroken) BreakNow(point, direction, force);
        });
    }

    private void BreakNow(Vector3 point, Vector3 direction, float force)
    {
        Vector3 linear = _velocity, angular = Vector3.Zero;
        if (_owner.DynamicBody != null && _scene != null && _scene.TryGetVelocity(_owner, out Vector3 bodyLinear, out Vector3 bodyAngular))
        {
            linear = bodyLinear;
            angular = bodyAngular;
        }
        Vector3 center = Center();
        float size = Size();
        // The pieces pass through the body it rode on (its ragdoll's head capsule is where they start).
        _fracture.SharedCollisionGroup = _target?.CollisionGroup ?? 0;
        int thrown = _fracture.Break(point, direction, force, linear, angular);

        GameFlow.SceneLogic?.BreakBursts.Burst(Effect, center, direction, size, EffectColor, ClampedEffectAmount, linear, _scene);
        Vector3 along = direction.LengthSquared() > 1e-8f ? Vector3.Normalize(direction) : Vector3.Zero;
        if (_target != null)
        {
            Collapse();
            if (RagdollOnBreak && _target.GetComponent<RagdollComponent>() is { Enabled: true } ragdoll)
            {
                ragdoll.Activate();
                ragdoll.AddImpulse(along * ragdoll.ClampedMass * Math.Min(force, 6f) * 0.5f, center);
            }
        }

        try { Broken?.Invoke(point, thrown); }
        catch (Exception ex) { Editor.EditorBridge.Log($"Breakable '{_owner?.Name}': a Broken handler threw: {ex.Message}"); }
    }

    // World centre of the model's bounds.
    private Vector3 Center()
    {
        if (_owner == null) return Vector3.Zero;
        Vector3 local = (_owner.BoundingBox.Min + _owner.BoundingBox.Max) * 0.5f * _owner.Scale;
        return _owner.Position + Vector3.TransformNormal(local, _owner.RotationMatrix);
    }

    private float Size()
    {
        Vector3 extent = (_owner.BoundingBox.Max - _owner.BoundingBox.Min) * _owner.Scale;
        return new Vector3(Math.Abs(extent.X), Math.Abs(extent.Y), Math.Abs(extent.Z)).Length() * 0.5f;
    }

    ////////////////////////////////////////////////////////////////////////////////
    //  FOLLOWING A GAMEOBJECT OR A BONE
    ////////////////////////////////////////////////////////////////////////////////

    // Finds the followed gameobject and keeps the owner's offset to it (or its bone) as placed in the editor.
    private void Attach()
    {
        _target = null;
        _bone = -1;
        string name = FollowObject?.Trim();
        if (string.IsNullOrEmpty(name) || _owner == null) return;
        List<BasicEntity> entities = GameFlow.SceneLogic?.ActiveScene?.BasicEntities;
        BasicEntity target = entities?.Find(e => e != _owner && e.Name == name);
        if (target == null)
        {
            Editor.EditorBridge.Log($"Breakable '{_owner.Name}': no gameobject named '{name}' to follow.");
            return;
        }
        SkinningData skeleton = SkinningData.From(target.Model);
        if (!string.IsNullOrWhiteSpace(FollowBone))
        {
            _bone = skeleton?.FindBone(FollowBone.Trim()) ?? -1;
            if (_bone < 0)
            {
                Editor.EditorBridge.Log($"Breakable '{_owner.Name}': '{target.Name}' has no bone '{FollowBone}'; following the gameobject.");
            }
        }
        // The pose it was placed against: the rest pose with the editor's posed bones.
        Matrix bone = Matrix.Identity;
        if (_bone >= 0)
        {
            var rest = new AnimationPlayer(skeleton) { Overrides = target.BonePose };
            rest.Evaluate(0);
            bone = rest.BoneTransforms[_bone];
        }
        Matrix followed = bone * WorldOf(target);
        _offset = _owner.RotationMatrix * Matrix.CreateTranslation(_owner.Position) * Matrix.Invert(followed);
        _target = target;
    }

    private void Follow()
    {
        if (_target == null) return;
        Matrix bone = Matrix.Identity;
        if (_bone >= 0)
        {
            Matrix[] bones = _target.Ragdoll is { IsActive: true, IsDisposed: false } ragdoll ? ragdoll.BoneModel : _target.BonePose.ModelBones(_target);
            if (bones == null || _bone >= bones.Length) return;
            bone = bones[_bone];
        }
        Matrix world = _offset * bone * WorldOf(_target);
        if (!world.Decompose(out _, out Quaternion rotation, out Vector3 position) || !float.IsFinite(position.X)) return;
        Matrix rotationMatrix = Matrix.CreateFromQuaternion(rotation);
        if (position != _owner.Position) _owner.Position = position;
        if (rotationMatrix != _owner.RotationMatrix) _owner.RotationMatrix = rotationMatrix;
    }

    private static Matrix WorldOf(BasicEntity e) => Matrix.CreateScale(e.Scale) * e.RotationMatrix * Matrix.CreateTranslation(e.Position);

    private void Collapse()
    {
        if (!CollapseBone || _target == null || _bone < 0) return;
        SkinningData skeleton = SkinningData.From(_target.Model);
        if (skeleton == null) return;
        Uncollapse();
        _collapsedOn = _target;
        _collapsedBone = skeleton.BoneNames[_bone];
        if (_collapsedOn.CollapsedBones.Add(_collapsedBone)) _collapsedOn.CollapsedBonesVersion++;
    }

    private void Uncollapse()
    {
        if (_collapsedOn != null && _collapsedOn.CollapsedBones.Remove(_collapsedBone)) _collapsedOn.CollapsedBonesVersion++;
        _collapsedOn = null;
        _collapsedBone = null;
    }
}
