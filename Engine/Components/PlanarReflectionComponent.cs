using System;
using System.Text.Json.Serialization;
using Engine.Entities;
using Microsoft.Xna.Framework;

namespace Engine.Components;

/// <summary>
/// A real mirror image on a flat glass gameobject (window, glass pane, mirror glass; a Forward shaded material):
/// every frame the opaque scene in front of it is drawn once more from the camera mirrored about its surface
/// (<see cref="Renderer.RenderModules.PlanarReflectionRenderModule"/>), and the glass shows that image instead
/// of the reflection probes. Unlike the probes it is exact for anything in front of the pane, moving objects and
/// the player included. The surface is the gameobject's face across its thinnest local axis that the camera sees.
/// Directions where nothing is drawn (the sky, beyond <see cref="RenderDistance"/>) keep the probes. At most
/// <see cref="Renderer.RenderModules.PlanarReflectionRenderModule.MaxReflections"/> panes draw a frame, nearest first.
/// </summary>
public sealed class PlanarReflectionComponent : GameComponent
{
    public const string TypeId = "planar-reflection";

    /// <summary>Size of the mirrored image against the screen (0.25..1); lower is cheaper and softer.</summary>
    public float ResolutionScale { get; set; } = 1;
    /// <summary>Gameobjects farther than this from the camera (metres) are left out of the mirror image.</summary>
    public float RenderDistance { get; set; } = 100;

    [JsonIgnore] public float ClampedResolutionScale => float.IsFinite(ResolutionScale) ? Math.Clamp(ResolutionScale, 0.25f, 1) : 1;
    [JsonIgnore] public float ClampedRenderDistance => float.IsFinite(RenderDistance) ? Math.Clamp(RenderDistance, 1, 10000) : 100;

    /// <summary>
    /// The reflecting face of <paramref name="owner"/> that <paramref name="camera"/> sees, its normal towards the
    /// camera, and a sphere around the gameobject. False when the camera looks at it edge-on.
    /// </summary>
    public static bool TryGetPlane(BasicEntity owner, Vector3 camera, out Plane plane, out BoundingSphere bounds)
    {
        plane = default;
        BoundingBox box = owner.BoundingBox;
        Vector3 extent = (box.Max - box.Min) * 0.5f * Abs(owner.Scale);
        Matrix rotation = owner.RotationMatrix;
        Vector3 center = owner.Position + Vector3.TransformNormal((box.Max + box.Min) * 0.5f * owner.Scale, rotation);
        bounds = new BoundingSphere(center, extent.Length());

        // The thinnest local axis is the surface normal; the face is half the thickness out from the centre
        Vector3 axis = extent.X <= extent.Y && extent.X <= extent.Z ? rotation.Right
            : extent.Y <= extent.Z ? rotation.Up : rotation.Backward;
        float half = Math.Min(extent.X, Math.Min(extent.Y, extent.Z));
        if (axis.LengthSquared() < 1e-8f) return false;
        axis.Normalize();

        float side = Vector3.Dot(camera - center, axis);
        if (Math.Abs(side) <= half + 0.001f) return false;
        if (side < 0) axis = -axis;
        Vector3 face = center + axis * half;
        plane = new Plane(axis, -Vector3.Dot(axis, face));
        return true;
    }

    private static Vector3 Abs(Vector3 v) => new Vector3(Math.Abs(v.X), Math.Abs(v.Y), Math.Abs(v.Z));
}
