using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Engine.Entities;
using Microsoft.Xna.Framework;

namespace Engine.Components;

/// <summary>Shape of an AO Proxy. Saved by name: never rename a value.</summary>
public enum AoProxyShape
{
    /// <summary>The armature's limbs for skinned models, else an ellipsoid around the model.</summary>
    Auto,
    /// <summary>An ellipsoid filling the model's bounds.</summary>
    Ellipsoid,
    /// <summary>A capsule along the longest side of the model's bounds.</summary>
    Capsule,
    /// <summary>One capsule per main bone of the skeleton (skinned models).</summary>
    Bones,
}

/// <summary>
/// Soft contact shadows for dynamic objects without SSAO (Source 2's ambient occlusion proxies,
/// CAmbientOcclusionProxySceneObject / vr_aoproxy_splat; Docs/Research/Source2Look phase 6.1): the gameobject is
/// represented by analytic ellipsoids or capsules that darken the indirect light (never the direct light) of the
/// surfaces around them, so a character or a crate sits on the floor. Fitted to the model's bounds, or to the
/// skeleton's limbs for skinned models, every frame. Culled beyond <see cref="CullDistance"/> times its size.
/// </summary>
public sealed class AoProxyComponent : GameComponent
{
    public const string TypeId = "ao-proxy";

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AoProxyShape Shape { get; set; } = AoProxyShape.Auto;
    /// <summary>0..1: how dark the occlusion gets.</summary>
    public float Intensity { get; set; } = 1;
    /// <summary>Scales the fitted shapes (more than 1 for a wider shadow).</summary>
    public float SizeScale { get; set; } = 1;
    /// <summary>How far the shadow reaches beyond the shape, in shape radii.</summary>
    public float Falloff { get; set; } = 2;
    /// <summary>Not drawn beyond this many times its size from the camera (r_aoproxy_cull_dist).</summary>
    public float CullDistance { get; set; } = 25;

    [JsonIgnore] public float ClampedIntensity => float.IsFinite(Intensity) ? Math.Clamp(Intensity, 0, 1) : 1;
    [JsonIgnore] public float ClampedSizeScale => float.IsFinite(SizeScale) ? Math.Clamp(SizeScale, 0.1f, 4) : 1;
    [JsonIgnore] public float ClampedFalloff => float.IsFinite(Falloff) ? Math.Clamp(Falloff, 0.25f, 8) : 2;
    [JsonIgnore] public float ClampedCullDistance => float.IsFinite(CullDistance) ? Math.Clamp(CullDistance, 1, 1000) : 25;
}

/// <summary>
/// The AO proxy shapes of a frame (Shaders/Common/AoProxies.fx), gathered from the AO Proxy components:
/// at most <see cref="MaxShapes"/>, nearest to the camera first.
/// </summary>
public sealed class AoProxyShapes
{
    public const int MaxShapes = 16;
    /// <summary>Most capsules one skinned gameobject contributes (its longest bones).</summary>
    public const int MaxBonesPerObject = 10;

    /// <summary>Ellipsoids: world to unit sphere. Capsules: unused.</summary>
    public readonly Matrix[] WorldToUnit = new Matrix[MaxShapes];
    /// <summary>xyz = capsule start, w = 0 ellipsoid / 1 capsule.</summary>
    public readonly Vector4[] A = new Vector4[MaxShapes];
    /// <summary>xyz = capsule end, w = capsule radius.</summary>
    public readonly Vector4[] B = new Vector4[MaxShapes];
    /// <summary>x = intensity, y = falloff in radii.</summary>
    public readonly Vector4[] Settings = new Vector4[MaxShapes];
    public int Count;

    private readonly List<(float Distance, Matrix Unit, Vector4 A, Vector4 B, Vector4 Settings)> _candidates = new();
    private readonly List<(Vector3 From, Vector3 To, int Bone)> _bones = new();

    public void Collect(IReadOnlyList<BasicEntity> entities, Vector3 camera)
    {
        _candidates.Clear();
        if (entities != null)
        {
            for (int i = 0; i < entities.Count; i++)
            {
                BasicEntity entity = entities[i];
                AoProxyComponent proxy = entity?.GetComponent<AoProxyComponent>();
                if (proxy == null || !proxy.Enabled || !entity.IsEnabled || entity.Model == null) continue;
                AddShapes(entity, proxy, camera);
            }
        }
        _candidates.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        Count = Math.Min(_candidates.Count, MaxShapes);
        for (int i = 0; i < Count; i++)
        {
            WorldToUnit[i] = _candidates[i].Unit;
            A[i] = _candidates[i].A;
            B[i] = _candidates[i].B;
            Settings[i] = _candidates[i].Settings;
        }
    }

    private void AddShapes(BasicEntity entity, AoProxyComponent proxy, Vector3 camera)
    {
        var settings = new Vector4(proxy.ClampedIntensity, proxy.ClampedFalloff, 0, 0);
        float scale = proxy.ClampedSizeScale;
        Matrix world = Matrix.CreateScale(entity.Scale) * entity.RotationMatrix * Matrix.CreateTranslation(entity.Position);
        BoundingBox bounds = entity.ModelDefinition?.BoundingBox ?? new BoundingBox(-Vector3.One, Vector3.One);
        Vector3 center = Vector3.Transform((bounds.Min + bounds.Max) * 0.5f, world);
        Vector3 half = (bounds.Max - bounds.Min) * 0.5f * new Vector3(Math.Abs(entity.Scale.X), Math.Abs(entity.Scale.Y), Math.Abs(entity.Scale.Z));
        float size = Math.Max(half.X, Math.Max(half.Y, half.Z)) * 2;
        float distance = Vector3.Distance(camera, center);
        if (distance > proxy.ClampedCullDistance * Math.Max(size, 0.1f)) return;

        AoProxyShape shape = proxy.Shape;
        bool skinned = Animation.SkinningData.From(entity.Model) != null;
        if (shape == AoProxyShape.Auto) shape = skinned ? AoProxyShape.Bones : AoProxyShape.Ellipsoid;
        if (shape == AoProxyShape.Bones && !skinned) shape = AoProxyShape.Capsule;

        switch (shape)
        {
            case AoProxyShape.Bones:
            {
                _bones.Clear();
                entity.BonePose.CollectLines(entity, _bones);
                _bones.Sort((x, y) => Vector3.DistanceSquared(y.From, y.To).CompareTo(Vector3.DistanceSquared(x.From, x.To)));
                int added = 0;
                foreach (var (from, to, _) in _bones)
                {
                    float length = Vector3.Distance(from, to);
                    if (length < 0.08f || added >= AoProxyShapes.MaxBonesPerObject) break;
                    float radius = MathHelper.Clamp(length * 0.3f, 0.04f, 0.2f) * scale;
                    Vector3 middle = (from + to) * 0.5f;
                    _candidates.Add((Vector3.Distance(camera, middle), Matrix.Identity, new Vector4(from, 1), new Vector4(to, radius), settings));
                    added++;
                }
                break;
            }
            case AoProxyShape.Capsule:
            {
                //Along the longest local axis, as thick as the next one
                Vector3 local = (bounds.Max - bounds.Min) * 0.5f * entity.Scale;
                Vector3 axis = Math.Abs(local.X) >= Math.Abs(local.Y) && Math.Abs(local.X) >= Math.Abs(local.Z) ? Vector3.UnitX
                    : Math.Abs(local.Y) >= Math.Abs(local.Z) ? Vector3.UnitY : Vector3.UnitZ;
                float length = Math.Abs(Vector3.Dot(local, axis));
                float radius = (Math.Abs(local.X) + Math.Abs(local.Y) + Math.Abs(local.Z) - length) * 0.5f * scale;
                Vector3 worldAxis = Vector3.TransformNormal(axis, entity.RotationMatrix);
                float halfLength = Math.Max(length - radius, 0);
                _candidates.Add((distance, Matrix.Identity, new Vector4(center - worldAxis * halfLength, 1),
                    new Vector4(center + worldAxis * halfLength, Math.Max(radius, 0.02f)), settings));
                break;
            }
            default:
            {
                //Ellipsoid through the bounds: unit sphere = the box's inscribed ellipsoid
                Vector3 radii = Vector3.Max((bounds.Max - bounds.Min) * 0.5f * scale, new Vector3(0.01f));
                Matrix unitToWorld = Matrix.CreateScale(radii) * Matrix.CreateTranslation((bounds.Min + bounds.Max) * 0.5f) * world;
                _candidates.Add((distance, Matrix.Invert(unitToWorld), new Vector4(center, 0), Vector4.Zero, settings));
                break;
            }
        }
    }
}
