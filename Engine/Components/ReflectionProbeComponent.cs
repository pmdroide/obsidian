using System.Text.Json.Serialization;
using Engine.Entities;
using Microsoft.Xna.Framework;

namespace Engine.Components;

/// <summary>
/// A local reflection probe (Source 2's env_cubemap / CEnvMapLightObjectDesc, Docs/Research/Source2Look phase 3.2):
/// a cubemap captured at the gameobject's position that reflections inside its box use instead of the scene's
/// global one, so a room reflects the room. The box is the gameobject's (a Cube with its position, rotation and
/// scale; the mesh is never drawn). Reflections are box projected (they line up with the walls) and fade in over
/// Blend Distance from the box faces; where probes overlap the higher Priority wins. With baked lighting, each probe
/// is normalised by the baked light here vs. at its capture point, so it never glows in the dark.
/// Captured when the scene loads, when the probe moves or changes, and on <see cref="RequestCapture"/>; one probe
/// per frame (Renderer.Lighting.ReflectionProbes).
/// </summary>
public sealed class ReflectionProbeComponent : GameComponent
{
    public const string TypeId = "reflection-probe";

    /// <summary>Metres inside the box over which the probe fades in.</summary>
    public float BlendDistance { get; set; } = 1;
    /// <summary>Project reflections onto the box (rooms); off = an infinitely far cubemap (open areas).</summary>
    public bool BoxProjection { get; set; } = true;
    /// <summary>Higher priority probes win where boxes overlap.</summary>
    public int Priority { get; set; }
    /// <summary>Texels per cube face: 64-512.</summary>
    public int Resolution { get; set; } = 128;

    /// <summary>Bumped to ask for a new capture (Inspector > Capture).</summary>
    [JsonIgnore] public int CaptureRequest { get; private set; }

    [JsonIgnore] public int ClampedResolution => Lighting.ClampResolution(Resolution);
    [JsonIgnore] public float ClampedBlendDistance => float.IsFinite(BlendDistance) ? System.Math.Clamp(BlendDistance, 0, 100) : 1;

    public void RequestCapture() => CaptureRequest++;

    public override void OnChanged(BasicEntity owner)
    {
        base.OnChanged(owner);
        PostProcessVolumeComponent.SetMeshHidden(owner, Enabled && owner?.IsEnabled == true);
        RequestCapture();
    }

    public override void OnRemoved(BasicEntity owner)
    {
        base.OnRemoved(owner);
        PostProcessVolumeComponent.SetMeshHidden(owner, false);
    }

    /// <summary>The probe's box: unit cube (-1..1) to world.</summary>
    public static Matrix BoxWorld(BasicEntity owner) => FogVolumeComponent.BoxWorld(owner);

    private static class Lighting
    {
        public static int ClampResolution(int value)
        {
            int size = 64;
            while (size < value && size < 512) size *= 2;
            return size;
        }
    }
}
