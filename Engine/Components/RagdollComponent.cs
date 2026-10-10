using System.Text.Json.Serialization;
using Engine.Animation;
using Engine.Editor;
using Engine.Entities;
using Engine.Physics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Components;

/// <summary>
/// Ragdoll physics for a skinned gameobject (a model built with the SkinnedModelProcessor). During Play its
/// bones get rigid bodies (<see cref="RagdollRig"/> picks them from the mesh): while the gameobject animates
/// they follow the pose kinematically, so balls bounce off the character and rays hit it. Once
/// <see cref="Activate"/>d (or on start, or when hit hard enough) the parts go limp: they fall, joined by
/// joints with human-like limits, the skin follows them and the gameobject moves with the hips.
/// <see cref="Deactivate"/> stands it back up where the hips lie and hands the pose back to the Animator.
/// </summary>
public sealed class RagdollComponent : GameComponent
{
    public const string TypeId = "ragdoll";
    public const float MinMass = 1f;
    // An impact never gives the whole ragdoll more than this speed (m/s), whatever hit it.
    private const float MaxImpactSpeed = 15f;

    /// <summary>Total mass of all parts, in kilograms.</summary>
    public float Mass { get; set; } = 70f;
    /// <summary>Go limp as soon as Play starts.</summary>
    public bool ActiveOnStart { get; set; }
    /// <summary>Go limp when a moving body hits it at least this fast (m/s); 0 never does.</summary>
    public float ImpactThreshold { get; set; }
    /// <summary>How strongly the joints resist bending: 0 is floppy, 1 nearly holds the pose.</summary>
    public float JointFriction { get; set; } = 0.2f;

    /// <summary>True while limp.</summary>
    [JsonIgnore] public bool IsActive => _ragdoll is { IsActive: true, IsDisposed: false };
    /// <summary>The bodies and joints during Play, or null.</summary>
    [JsonIgnore] public Ragdoll Ragdoll => _ragdoll is { IsDisposed: false } ? _ragdoll : null;

    public float ClampedMass => float.IsFinite(Mass) ? Math.Max(Mass, MinMass) : 70f;
    public float ClampedImpactThreshold => float.IsFinite(ImpactThreshold) ? Math.Max(ImpactThreshold, 0) : 0;
    public float ClampedJointFriction => float.IsFinite(JointFriction) ? Math.Clamp(JointFriction, 0, 1) : 0.2f;

    /// <summary>Rig for gameobjects without a loaded model (GPU-free checks in Tests/Components).</summary>
    internal static Func<BasicEntity, RagdollRig> FallbackRig;

    private BasicEntity _owner;
    private ScenePhysics _scene;
    private Ragdoll _ragdoll;
    private SkinnedMeshInstance _skin;
    private bool _wantActive;
    private bool _failed;
    private bool _rebuild;

    public override void OnStart(BasicEntity owner)
    {
        OnStop();
        if (owner == null) return; // the camera has no skeleton
        _owner = owner;
        _wantActive = ActiveOnStart;
    }

    public override void OnUpdate(BasicEntity owner, GameTime time)
    {
        if (owner == null) return;
        if (owner != _owner)
        {
            OnStop();
            _owner = owner;
        }
        // New settings take effect while standing; a limp ragdoll keeps its bodies until it recovers.
        if (_rebuild && !IsActive)
        {
            RemoveRagdoll();
            _rebuild = false;
        }
        if (_ragdoll == null || _ragdoll.IsDisposed)
        {
            _ragdoll = null;
            if (_failed || !Build()) return;
        }
        _ragdoll.DrivePose = AnimatedPose();
        if (_wantActive && !_ragdoll.IsActive) Activate();
    }

    /// <summary>Goes limp (during Play). Called before the ragdoll exists, it goes limp as soon as it does.</summary>
    public void Activate()
    {
        _wantActive = true;
        if (_ragdoll == null || _ragdoll.IsDisposed || _ragdoll.IsActive) return;
        if (_skin == null)
        {
            GraphicsDevice device = FindDevice(_owner.Model);
            if (device != null)
            {
                _skin = new SkinnedMeshInstance(device, _owner.Model);
                if (!_skin.HasSkinnedBuffers)
                {
                    _skin.Dispose();
                    _skin = null;
                }
            }
        }
        _ragdoll.Activate();
        _ragdoll.UpdatePose();
        OnPosed();
    }

    /// <summary>
    /// Stops being limp: the gameobject stands up where the hips lie (on the ground below them, keeping its
    /// facing) and the Animator, if any, drives the pose again.
    /// </summary>
    public void Deactivate()
    {
        _wantActive = false;
        if (!IsActive) return;

        Vector3 hips = _ragdoll.RootBonePosition;
        Vector3 standing = _owner.Position;
        if (_scene.Raycast(hips + Vector3.UnitZ * 0.1f, -Vector3.UnitZ, 10f, out RaycastHit ground, ignore: _owner))
            standing.Z = ground.Point.Z;
        _ragdoll.Deactivate();
        _owner.Position = standing;
        ReleaseSkin();
    }

    /// <summary>Instant change of momentum (kg*m/s) on the part nearest <paramref name="worldPoint"/> (or the hips). Only while limp.</summary>
    public void AddImpulse(Vector3 impulse, Vector3? worldPoint = null)
    {
        if (!IsActive) return;
        int part = worldPoint.HasValue ? _ragdoll.NearestPart(worldPoint.Value) : 0;
        _ragdoll.ApplyImpulse(part, impulse, worldPoint);
    }

    public override void OnChanged(BasicEntity owner)
    {
        base.OnChanged(owner);
        _rebuild = true;
        _failed = false;
    }

    public override void OnStop()
    {
        RemoveRagdoll();
        _skin?.Dispose();
        _skin = null;
        _wantActive = false;
        _failed = false;
        _rebuild = false;
    }

    private bool Build()
    {
        _scene = _owner.PhysicsScene ?? ScenePhysics.Current;
        if (_scene == null) return false;
        RagdollRig rig = _owner.Model != null ? RagdollRig.For(_owner.Model) : FallbackRig?.Invoke(_owner);
        if (rig == null)
        {
            EditorBridge.Log($"Ragdoll: '{_owner.Name}' has no skinned skeleton; build its model with the SkinnedModelProcessor.");
            _failed = true; // an inspector edit retries
            return false;
        }
        _ragdoll = _scene.CreateRagdoll(_owner, rig, AnimatedPose(rig), ClampedMass, ClampedJointFriction);
        _ragdoll.Posed = OnPosed;
        _ragdoll.Hit = OnHit;
        return true;
    }

    private void RemoveRagdoll()
    {
        if (_ragdoll != null)
        {
            ReleaseSkin();
            _scene?.RemoveRagdoll(_ragdoll);
        }
        _ragdoll = null;
    }

    // The skin goes back to the Animator (which re-poses itself) or to the bind pose.
    private void ReleaseSkin()
    {
        if (_owner == null || _skin == null || _owner.WorldTransform.Skin != _skin) return;
        _owner.WorldTransform.Skin = null;
        _owner.WorldTransform.HasChanged = true;
    }

    private Matrix[] AnimatedPose(RagdollRig rig = null)
    {
        AnimatorComponent animator = _owner.GetComponent<AnimatorComponent>();
        if (animator is { Enabled: true, IsPlaying: true, Player: not null }) return animator.Player.BoneTransforms;
        return (rig ?? _ragdoll.Rig).BindModel;
    }

    private void OnPosed()
    {
        if (_skin == null || _ragdoll == null) return;
        _skin.Update(_ragdoll.SkinTransforms);
        _owner.WorldTransform.Skin = _skin;
        // Skinned vertices moved: redo culling and redraw shadow maps this frame.
        _owner.WorldTransform.HasChanged = true;
    }

    private void OnHit(int part, PhysicsContact contact, BasicEntity other, float otherMass)
    {
        float threshold = ClampedImpactThreshold;
        if (IsActive || !(threshold > 0) || contact.ImpactSpeed < threshold || !(otherMass > 0)) return;
        Activate();
        // The other body bounced off the animated (immovable) part; the ragdoll takes that momentum instead.
        float impulse = Math.Min(otherMass * contact.ImpactSpeed, _ragdoll.Mass * MaxImpactSpeed);
        _ragdoll.ApplyImpulse(part, contact.Normal * impulse, contact.Point);
    }

    private static GraphicsDevice FindDevice(Model model)
    {
        if (model == null) return null;
        foreach (ModelMesh mesh in model.Meshes)
            foreach (ModelMeshPart part in mesh.MeshParts)
                if (part.VertexBuffer != null) return part.VertexBuffer.GraphicsDevice;
        return null;
    }
}
