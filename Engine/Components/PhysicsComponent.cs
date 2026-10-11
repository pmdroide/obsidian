using Engine.Entities;
using Engine.Physics;
using Microsoft.Xna.Framework;
using System.Text.Json.Serialization;

namespace Engine.Components;

/// <summary>
/// Rigid body settings. <see cref="ScenePhysics"/> builds, rebuilds and removes the
/// BEPU body every physics update to match this component, so edits need no callback.
/// </summary>
public sealed class PhysicsComponent : GameComponent
{
    public const string TypeId = "physics";
    public const float MinMass = 0.001f;

    /// <summary>Static = immovable triangle-mesh collider; Dynamic = convex hull with gravity.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PhysicsBodyType BodyType { get; set; } = PhysicsBodyType.Dynamic;
    /// <summary>Dynamic bodies only, in kilograms.</summary>
    public float Mass { get; set; } = 1f;
    /// <summary>Dynamic bodies only: float in gameobjects with the Water role.</summary>
    public bool Buoyancy { get; set; }
    /// <summary>Upward push when fully under water, relative to the body's weight: 2 floats half-submerged, below 1 sinks.</summary>
    public float BuoyancyStrength { get; set; } = 2f;
    /// <summary>How strongly water slows the body's movement and spin (fraction removed per second).</summary>
    public float WaterDrag { get; set; } = 3f;
    /// <summary>
    /// Overlaps instead of colliding: scripts get OnTriggerEnter/Stay/Exit, nothing is pushed and raycasts
    /// pass through. A Static trigger uses the model's convex hull, so a body fully inside still counts.
    /// </summary>
    public bool IsTrigger { get; set; }
    /// <summary>Dynamic bodies only: contacts and impulses never tip or spin it (characters, upright props).</summary>
    public bool FreezeRotation { get; set; }
    /// <summary>
    /// Size of the collider's box in model units (before the gameobject's Scale). The model's collider shape
    /// is stretched about its bounds centre to fill it; 0 = the model's own bounds (see <see cref="ResetColliderSize"/>).
    /// </summary>
    public float ColliderSizeX { get; set; }
    public float ColliderSizeY { get; set; }
    public float ColliderSizeZ { get; set; }

    /// <summary>The body ScenePhysics should build: None while disabled.</summary>
    public PhysicsBodyType ActiveBodyType => Enabled ? BodyType : PhysicsBodyType.None;
    public float ClampedMass => float.IsFinite(Mass) ? Math.Max(Mass, MinMass) : 1f;
    public float ClampedBuoyancyStrength => float.IsFinite(BuoyancyStrength) ? Math.Clamp(BuoyancyStrength, 0, 20) : 2f;
    public float ClampedWaterDrag => float.IsFinite(WaterDrag) ? Math.Clamp(WaterDrag, 0, 20) : 3f;

    /// <summary>Back to the model's own bounds; the next <see cref="OnChanged"/> writes them in.</summary>
    public void ResetColliderSize() => ColliderSizeX = ColliderSizeY = ColliderSizeZ = 0;

    /// <summary>The collider's box size in model units: the stored size, or the model's bounds where unset.</summary>
    public Vector3 ColliderSizeFor(BasicEntity owner)
    {
        Vector3 bounds = owner != null ? owner.BoundingBox.Max - owner.BoundingBox.Min : Vector3.One;
        return new Vector3(ValidSize(ColliderSizeX, bounds.X), ValidSize(ColliderSizeY, bounds.Y), ValidSize(ColliderSizeZ, bounds.Z));
    }

    /// <summary>Per-axis stretch from the model's bounds to the collider size; 1 where they match.</summary>
    public Vector3 ColliderStretchFor(BasicEntity owner)
    {
        if (owner == null) return Vector3.One;
        Vector3 bounds = owner.BoundingBox.Max - owner.BoundingBox.Min;
        Vector3 size = ColliderSizeFor(owner);
        return new Vector3(Stretch(size.X, bounds.X), Stretch(size.Y, bounds.Y), Stretch(size.Z, bounds.Z));
    }

    private static float ValidSize(float size, float fallback) => float.IsFinite(size) && size > 0 ? size : Math.Max(fallback, 0);

    // A flat axis (a plane's thickness) has nothing to stretch; tiny differences are float noise from a round trip.
    private static float Stretch(float size, float bounds) =>
        bounds > 1e-5f && Math.Abs(size - bounds) > bounds * 1e-4f ? size / bounds : 1f;

    // ScenePhysics picks up every change (including Enabled) on its next update.
    // Unset sizes are filled with the model's bounds so the inspector shows real numbers.
    public override void OnChanged(BasicEntity owner)
    {
        if (owner == null) return;
        Vector3 size = ColliderSizeFor(owner);
        ColliderSizeX = size.X;
        ColliderSizeY = size.Y;
        ColliderSizeZ = size.Z;
    }
}
