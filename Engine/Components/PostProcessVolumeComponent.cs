using System.Collections.Generic;
using System.Text.Json.Serialization;
using Engine.Entities;
using Engine.Logic;
using Microsoft.Xna.Framework;

namespace Engine.Components;

/// <summary>
/// Post processing for an area (Source 2's post-processing volume, Docs/Research/Source2Look phase 5.1): inside the
/// gameobject's box (a Cube with its position, rotation and scale; the mesh is never drawn) the camera's post
/// processing moves to this volume's values, fading in over <see cref="BlendDistance"/> outside the box. Only the
/// settings listed in <see cref="Overrides"/> change; the rest keep the scene's (Environment > Post Processing).
/// Values come from <see cref="Profile"/>, or from a <c>.post</c> asset when <see cref="ProfilePath"/> is set.
/// <see cref="Infinite"/> volumes apply everywhere. Volumes blend in Priority order, the highest last. Scripts fade
/// <see cref="Weight"/> for effects like a death or pause screen.
/// </summary>
public sealed class PostProcessVolumeComponent : GameComponent
{
    public const string TypeId = "post-volume";

    /// <summary>Applies everywhere instead of inside the box.</summary>
    public bool Infinite { get; set; }
    /// <summary>Metres outside the box over which the volume fades in.</summary>
    public float BlendDistance { get; set; } = 2;
    /// <summary>0..1: how much of the volume's values are used at full strength.</summary>
    public float Weight { get; set; } = 1;
    /// <summary>Volumes are blended lowest priority first; the highest wins where they overlap.</summary>
    public int Priority { get; set; }
    /// <summary>Content-relative <c>.post</c> profile (PostProfiles/Dark.post); empty = <see cref="Profile"/>.</summary>
    public string ProfilePath { get; set; }
    /// <summary>The volume's own values (used without a Profile Path).</summary>
    public PostProcessSettings Profile { get; set; } = new PostProcessSettings();
    /// <summary>Names of the <see cref="PostProcessSettings"/> this volume overrides (e.g. "Exposure", "Saturation").</summary>
    public List<string> Overrides { get; set; } = new List<string>();

    [JsonIgnore] public float ClampedWeight => float.IsFinite(Weight) ? MathHelper.Clamp(Weight, 0, 1) : 0;

    /// <summary>The values in effect: the profile file's when it loads, else <see cref="Profile"/>.</summary>
    public PostProcessSettings Values => (string.IsNullOrWhiteSpace(ProfilePath) ? null : PostProfile.Load(ProfilePath)) ?? Profile;

    public override void OnChanged(BasicEntity owner)
    {
        base.OnChanged(owner);
        Profile ??= new PostProcessSettings();
        Overrides ??= new List<string>();
        SetMeshHidden(owner, Enabled && owner?.IsEnabled == true);
    }

    public override void OnRemoved(BasicEntity owner)
    {
        base.OnRemoved(owner);
        SetMeshHidden(owner, false);
    }

    /// <summary>Whether this volume overrides the named setting.</summary>
    public bool HasOverride(string setting) => Overrides != null && Overrides.Exists(o => string.Equals(o, setting, System.StringComparison.OrdinalIgnoreCase));

    /// <summary>Weight of this volume for a camera at <paramref name="position"/>: full inside, fading out over Blend Distance.</summary>
    public float WeightAt(BasicEntity owner, Vector3 position)
    {
        float weight = ClampedWeight;
        if (Infinite || weight <= 0 || owner == null) return weight;
        Vector3 scale = owner.Scale;
        if (System.Math.Abs(scale.X) < 1e-4f || System.Math.Abs(scale.Y) < 1e-4f || System.Math.Abs(scale.Z) < 1e-4f) return 0;
        // Distance outside the box in world metres: box space is the unit cube, so scale the overshoot back
        Vector3 p = Vector3.Transform(position, Matrix.Invert(FogVolumeComponent.BoxWorld(owner)));
        Vector3 outside = Vector3.Max(new Vector3(System.Math.Abs(p.X), System.Math.Abs(p.Y), System.Math.Abs(p.Z)) - Vector3.One, Vector3.Zero);
        float distance = (outside * new Vector3(System.Math.Abs(scale.X), System.Math.Abs(scale.Y), System.Math.Abs(scale.Z))).Length();
        if (distance <= 0) return weight;
        float blend = float.IsFinite(BlendDistance) ? System.Math.Max(BlendDistance, 0) : 0;
        return blend <= 0 ? 0 : weight * MathHelper.Clamp(1 - distance / blend, 0, 1);
    }

    internal static void SetMeshHidden(BasicEntity owner, bool hidden)
    {
        if (owner == null || owner.WorldTransform.Hidden == hidden) return;
        owner.WorldTransform.Hidden = hidden;
        owner.WorldTransform.HasChanged = true;
    }
}
