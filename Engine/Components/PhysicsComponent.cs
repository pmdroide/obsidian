using Engine.Entities;
using Engine.Physics;
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

    /// <summary>The body ScenePhysics should build: None while disabled.</summary>
    public PhysicsBodyType ActiveBodyType => Enabled ? BodyType : PhysicsBodyType.None;
    public float ClampedMass => float.IsFinite(Mass) ? Math.Max(Mass, MinMass) : 1f;
    public float ClampedBuoyancyStrength => float.IsFinite(BuoyancyStrength) ? Math.Clamp(BuoyancyStrength, 0, 20) : 2f;
    public float ClampedWaterDrag => float.IsFinite(WaterDrag) ? Math.Clamp(WaterDrag, 0, 20) : 3f;

    // ScenePhysics picks up every change (including Enabled) on its next update.
    public override void OnChanged(BasicEntity owner) { }
}
