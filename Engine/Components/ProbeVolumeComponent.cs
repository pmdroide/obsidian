using System.Text.Json.Serialization;
using Engine.Entities;
using Microsoft.Xna.Framework;

namespace Engine.Components;

/// <summary>
/// A detail probe volume (Source 2's light probe volumes, CSceneLightProbeVolumeObject / ChooseLightProbeVolume;
/// Docs/Research/Source2Look phase 4.1): baked with the scene's main volume (Inspector > Lighting > Bake) over the
/// axis-aligned bounds of the gameobject's box (a Cube with its position, rotation and scale; the mesh is never drawn),
/// with its own, usually finer, probe spacing. Where volumes overlap the higher Priority wins; each fades in over
/// Blend Margin metres inside its box. Up to three are used (the main volume is the fourth).
/// </summary>
public sealed class ProbeVolumeComponent : GameComponent
{
    public const string TypeId = "probe-volume";

    /// <summary>Metres between probes (the main volume uses Lighting > Probe Spacing).</summary>
    public float ProbeSpacing { get; set; } = 1;
    /// <summary>Higher priority volumes win where they overlap.</summary>
    public int Priority { get; set; }
    /// <summary>Metres inside the box over which the volume fades in.</summary>
    public float BlendMargin { get; set; } = 1;

    [JsonIgnore] public float ClampedSpacing => float.IsFinite(ProbeSpacing) ? System.Math.Clamp(ProbeSpacing, 0.1f, 20) : 1;
    [JsonIgnore] public float ClampedMargin => float.IsFinite(BlendMargin) ? System.Math.Clamp(BlendMargin, 0, 20) : 1;

    public override void OnChanged(BasicEntity owner)
    {
        base.OnChanged(owner);
        PostProcessVolumeComponent.SetMeshHidden(owner, Enabled && owner?.IsEnabled == true);
    }

    public override void OnRemoved(BasicEntity owner)
    {
        base.OnRemoved(owner);
        PostProcessVolumeComponent.SetMeshHidden(owner, false);
    }

    /// <summary>World-space bounds the probes are laid out in: the axis-aligned box around the gameobject's box.</summary>
    public static BoundingBox Bounds(BasicEntity owner)
    {
        Matrix box = FogVolumeComponent.BoxWorld(owner);
        Vector3 min = new Vector3(float.MaxValue), max = new Vector3(float.MinValue);
        for (int i = 0; i < 8; i++)
        {
            var corner = new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1);
            Vector3 p = Vector3.Transform(corner, box);
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }
        return new BoundingBox(min, max);
    }
}
