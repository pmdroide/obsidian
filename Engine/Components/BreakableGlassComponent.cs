using System.Text.Json.Serialization;
using Engine.Entities;
using Engine.Logic;
using Engine.Physics;
using Microsoft.Xna.Framework;

namespace Engine.Components;

/// <summary>
/// Makes a flat gameobject (a thin Cube or any pane; the thinnest local side is its normal) break like
/// Half-Life: Alyx's <c>func_shatterglass</c> during Play. The first hit cracks the whole pane into Voronoi panels
/// (cells of <see cref="CrackSize"/>, Valve's 15 in grid); the hit punches a hole whose radial shards fly off,
/// panels no longer joined to the frame fall, and later hits break the panels that are left. Thrown bodies break it
/// on impact and carry on through; scripts call <see cref="Shatter"/> (bullets, melee, blasts). Shards use the
/// gameobject's material, fall as physics bodies and vanish after <see cref="DebrisLifetime"/>. Give it a Static
/// Physics component so things can hit it. Stop puts the pane back.
/// </summary>
public sealed class BreakableGlassComponent : GameComponent
{
    public const string TypeId = "breakable-glass";

    /// <summary>Size (m) of the panels the first hit cracks the pane into (Half-Life: Alyx: 15 in = 0.381 m).</summary>
    public float CrackSize { get; set; } = 0.381f;
    /// <summary>Radius (m) of a bullet hole; thrown bodies punch holes their own size.</summary>
    public float HoleRadius { get; set; } = 0.06f;
    /// <summary>Speed (m/s) a body must hit the pane with to break it; 0 = only scripts break it.</summary>
    public float ImpactSpeed { get; set; } = 4f;
    /// <summary>Momentum (kg·m/s, mass x speed) a body must hit with to break it.</summary>
    public float ImpactMomentum { get; set; } = 2f;
    /// <summary>Seconds fallen shards last (Half-Life: Alyx: 15). The scene keeps at most 150.</summary>
    public float DebrisLifetime { get; set; } = 15f;
    /// <summary>Panels no longer joined to the pane's border through other panels fall.</summary>
    public bool DropLooseShards { get; set; } = true;
    /// <summary>Cracked panels lean slightly, so reflections break up along the cracks.</summary>
    public bool Distort { get; set; } = true;
    /// <summary>Shards left in the frame stop bodies (and break when hit hard enough).</summary>
    public bool SolidShards { get; set; } = true;
    /// <summary>A hit finds a panel within this many times its size around its centre (Half-Life: Alyx: 2).</summary>
    public float HitTolerance { get; set; } = 2f;

    public float ClampedCrackSize => float.IsFinite(CrackSize) ? Math.Clamp(CrackSize, 0.05f, 10f) : 0.381f;
    public float ClampedHoleRadius => float.IsFinite(HoleRadius) ? Math.Clamp(HoleRadius, 0.01f, 2f) : 0.06f;
    public float ClampedImpactSpeed => float.IsFinite(ImpactSpeed) ? Math.Max(ImpactSpeed, 0) : 4f;
    public float ClampedImpactMomentum => float.IsFinite(ImpactMomentum) ? Math.Max(ImpactMomentum, 0) : 2f;
    public float ClampedDebrisLifetime => float.IsFinite(DebrisLifetime) ? Math.Clamp(DebrisLifetime, 0.1f, 600f) : 15f;
    public float ClampedHitTolerance => float.IsFinite(HitTolerance) ? Math.Clamp(HitTolerance, 1f, 5f) : 2f;

    /// <summary>The pane has cracked (during Play).</summary>
    [JsonIgnore] public bool IsBroken => _glass is { IsBroken: true, IsDisposed: false };
    /// <summary>Shards still hanging in the frame (0 before the first hit).</summary>
    [JsonIgnore] public int ShardCount => _glass?.FrameShardCount ?? 0;
    /// <summary>Falling shards of this pane.</summary>
    [JsonIgnore] public int DebrisCount => _glass?.DebrisCount ?? 0;
    /// <summary>The simulation during Play, or null.</summary>
    [JsonIgnore] public GlassShatter Glass => _glass;

    /// <summary>After each hit that broke glass: world point, stress, shards thrown.</summary>
    public event Action<Vector3, GlassStress, int> Shattered;

    private BasicEntity _owner;
    private GlassShatter _glass;
    private ScenePhysics _scene;
    private float _time;
    private BasicEntity _lastHitter;
    private float _lastHitTime = float.NegativeInfinity;

    public override void OnStart(BasicEntity owner)
    {
        OnStop();
        _owner = owner; // null on the camera, which has no pane
        _time = 0;
        if (owner != null) Build();
    }

    public override void OnUpdate(BasicEntity owner, GameTime time)
    {
        if (owner == null) return;
        if (owner != _owner)
        {
            OnStop();
            _owner = owner;
        }
        _time += (float)time.ElapsedGameTime.TotalSeconds;
        if (_glass == null || _glass.IsDisposed) Build();
        if (_glass == null) return;
        ApplySettings();
        // Hits from scripts this frame.
        _glass.ApplyPending();
    }

    /// <summary>
    /// Breaks the pane at a world point (bullets: <see cref="GlassStress.Ballistic"/>; melee or thrown:
    /// <see cref="GlassStress.Blunt"/> with a <paramref name="radius"/>; blasts: <see cref="GlassStress.Explosive"/>).
    /// <paramref name="direction"/> is the way the hit travels; <paramref name="speed"/> (m/s) how fast shards leave.
    /// Only during Play. Returns false outside Play.
    /// </summary>
    public bool Shatter(Vector3 point, Vector3 direction, GlassStress stress = GlassStress.Ballistic, float speed = 4f, float radius = 0)
    {
        if (!Enabled || _owner == null) return false;
        if (_glass == null || _glass.IsDisposed) Build();
        if (_glass == null) return false;
        ApplySettings();
        _glass.Queue(point, direction, stress, Math.Max(speed, 0), radius);
        _glass.ApplyPending();
        return true;
    }

    /// <summary>Shatters at a ray's hit (e.g. <c>Raycast(CameraRay, ...)</c>) along the ray.</summary>
    public bool Shatter(RaycastHit hit, Vector3 direction, GlassStress stress = GlassStress.Ballistic, float speed = 4f) =>
        Shatter(hit.Point, direction, stress, speed);

    /// <summary>Puts the pane back whole (during Play), removing its shards.</summary>
    public void Restore()
    {
        if (_owner == null) return;
        Remove();
        Build();
    }

    public override void OnChanged(BasicEntity owner)
    {
        base.OnChanged(owner);
        if (_glass != null && !Enabled) Remove();
    }

    public override void OnStop()
    {
        Remove();
        _owner = null;
        _lastHitter = null;
    }

    private void Build()
    {
        if (!Enabled || _owner == null) return;
        _scene = _owner.PhysicsScene ?? ScenePhysics.Current;
        if (_scene == null) return;
        _glass = _scene.CreateGlass(_owner, GameFlow.SceneLogic?.MeshMaterialLibrary);
        _glass.Hit = OnHit;
        _glass.Shattered += OnShattered;
        ApplySettings();
    }

    private void Remove()
    {
        if (_glass == null) return;
        _glass.Shattered -= OnShattered;
        _glass.Hit = null;
        if (_scene != null) _scene.RemoveGlass(_glass);
        else _glass.Dispose();
        _glass = null;
        _scene = null;
    }

    private void ApplySettings()
    {
        _glass.CellSize = ClampedCrackSize;
        _glass.HoleRadius = ClampedHoleRadius;
        _glass.HitTolerance = ClampedHitTolerance;
        _glass.DebrisLifetime = ClampedDebrisLifetime;
        _glass.DropUnsupported = DropLooseShards;
        _glass.Distort = Distort;
        _glass.SolidShards = SolidShards;
    }

    private void OnHit(PhysicsContact contact, BasicEntity other, float otherMass)
    {
        float speed = ClampedImpactSpeed;
        if (!Enabled || !(speed > 0) || !(otherMass > 0) || contact.IsTrigger) return;
        if (contact.ImpactSpeed < speed || otherMass * contact.ImpactSpeed < ClampedImpactMomentum) return;
        // A body touching the pane on several contacts in one step counts once.
        if (other == _lastHitter && _time == _lastHitTime) return;
        _lastHitter = other;
        _lastHitTime = _time;

        // A hole the body fits through: its bounding sphere and a little more.
        Vector3 size = (other.BoundingBox.Max - other.BoundingBox.Min) * 0.5f * other.Scale;
        float radius = Math.Clamp(size.Length() * 1.15f, ClampedHoleRadius, 1.5f);
        // Seen from the pane, the normal points from the other body into the glass: the way it travels.
        _glass.Queue(contact.Point, contact.Normal, GlassStress.Blunt, contact.ImpactSpeed * 0.5f, radius, other, contact.ImpactSpeed);
    }

    private void OnShattered(Vector3 point, GlassStress stress, int thrown)
    {
        try { Shattered?.Invoke(point, stress, thrown); }
        catch (Exception ex) { Editor.EditorBridge.Log($"Breakable Glass of '{_owner?.Name}': a Shattered handler threw: {ex.Message}"); }
    }
}
