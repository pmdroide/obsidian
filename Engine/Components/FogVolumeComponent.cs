using System.Text.Json.Serialization;
using Engine.Entities;
using Microsoft.Xna.Framework;

namespace Engine.Components;

/// <summary>
/// Local volumetric fog: adds froxel fog density inside a box, the gameobject's position, rotation and scale
/// (a Cube with that scale). Its fog ignores the scene fog's Start/Full distance ramp, so a dusty room shows
/// light shafts right in front of the camera while the open air keeps the scene's thin haze. The gameobject's
/// own mesh is never drawn while the component is enabled (picking included, select it in the hierarchy).
/// Works in the editor and in Play. Shafts need the light's shadows, so keep the box inside the sun's shadow box.
/// </summary>
public sealed class FogVolumeComponent : GameComponent
{
    public const string TypeId = "fog-volume";
    /// <summary>Volumes sent to the froxel shader per frame; must match MAX_FOG_VOLUMES in Froxel.fx.</summary>
    public const int MaxVolumes = 8;

    /// <summary>Fog density added inside the box, in the scene fog's Density units (scene default 0.03).</summary>
    public float Density { get; set; } = 0.15f;
    /// <summary>Fraction of the box (from each face inwards) over which the density fades in; 0 = hard edges.</summary>
    public float EdgeFade { get; set; } = 0.1f;

    [JsonIgnore] public float ClampedDensity => float.IsFinite(Density) ? Math.Clamp(Density, 0, 5) : 0;
    [JsonIgnore] public float ClampedEdgeFade => float.IsFinite(EdgeFade) ? Math.Clamp(EdgeFade, 0, 1) : 0;

    public override void OnChanged(BasicEntity owner)
    {
        base.OnChanged(owner);
        SetMeshHidden(owner, Enabled && owner?.IsEnabled == true);
    }

    public override void OnRemoved(BasicEntity owner)
    {
        base.OnRemoved(owner);
        SetMeshHidden(owner, false);
    }

    /// <summary>
    /// Fills <paramref name="worldToBox"/>/<paramref name="settings"/> with the enabled volumes of
    /// <paramref name="entities"/> (box space is the unit cube -1..1; settings = density, edge fade) and
    /// keeps their meshes hidden (scene loads attach components without <see cref="OnChanged"/>).
    /// Returns how many were written, at most <see cref="MaxVolumes"/>.
    /// </summary>
    public static int Collect(IReadOnlyList<BasicEntity> entities, Matrix[] worldToBox, Vector4[] settings)
    {
        int count = 0;
        if (entities == null) return 0;
        for (int i = 0; i < entities.Count; i++)
        {
            BasicEntity entity = entities[i];
            FogVolumeComponent volume = entity?.GetComponent<FogVolumeComponent>();
            if (volume == null) continue;
            bool active = volume.Enabled && entity.IsEnabled;
            SetMeshHidden(entity, active);
            if (!active || volume.ClampedDensity <= 0 || count >= Math.Min(MaxVolumes, worldToBox.Length)) continue;
            Vector3 scale = entity.Scale;
            if (Math.Abs(scale.X) < 1e-4f || Math.Abs(scale.Y) < 1e-4f || Math.Abs(scale.Z) < 1e-4f) continue;
            worldToBox[count] = Matrix.Invert(BoxWorld(entity));
            settings[count] = new Vector4(volume.ClampedDensity, volume.ClampedEdgeFade, 0, 0);
            count++;
        }
        return count;
    }

    /// <summary>The box's world matrix: unit cube (-1..1) to world, like a Cube gameobject.</summary>
    public static Matrix BoxWorld(BasicEntity owner) =>
        Matrix.CreateScale(owner.Scale) * owner.RotationMatrix * Matrix.CreateTranslation(owner.Position);

    /// <summary>The density this volume adds at <paramref name="world"/> (matches FogVolumeDensity in Froxel.fx).</summary>
    public float DensityAt(BasicEntity owner, Vector3 world)
    {
        Vector3 p = Vector3.Transform(world, Matrix.Invert(BoxWorld(owner)));
        float inside = 1 - Math.Max(Math.Abs(p.X), Math.Max(Math.Abs(p.Y), Math.Abs(p.Z)));
        if (inside < 0) return 0;
        float fade = ClampedEdgeFade;
        return ClampedDensity * (fade > 0 ? Math.Clamp(inside / fade, 0, 1) : 1);
    }

    private static void SetMeshHidden(BasicEntity owner, bool hidden)
    {
        if (owner == null || owner.WorldTransform.Hidden == hidden) return;
        owner.WorldTransform.Hidden = hidden;
        owner.WorldTransform.HasChanged = true;
    }
}
