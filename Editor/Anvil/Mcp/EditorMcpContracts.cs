using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Engine.Editor;
using Microsoft.Xna.Framework;

namespace Anvil.Mcp;

public sealed record EditorVector(float X, float Y, float Z)
{
    public Vector3 ToEngine() => new(X, Y, Z);
    public static EditorVector From(Vector3 value) => new(value.X, value.Y, value.Z);
}

public sealed record EditorRotation(float X, float Y, float Z, float W)
{
    public Quaternion ToEngine() => new(X, Y, Z, W);
    public static EditorRotation From(Matrix value)
    {
        var q = value == default ? Quaternion.Identity : Quaternion.CreateFromRotationMatrix(value);
        return new(q.X, q.Y, q.Z, q.W);
    }
}

public sealed record EditorStatus(string InstanceId, bool Ready, bool ReadOnly, string? SceneGeneration,
    long Revision, DateTimeOffset? CapturedAt, string? SceneName, string? ScenePath,
    bool IsDirty, string? Mode, int? SelectedId);
public sealed record EditorComponent(string Type, Guid InstanceId, JsonElement Data);
public sealed record EditorBone(int Id, string Name, int ParentIndex, EditorVector Position, EditorRotation Rotation);
public sealed record EditorObject(int Id, string Name, string Kind, EditorVector Position, EditorRotation Rotation,
    EditorVector Scale, bool Enabled, IReadOnlyList<EditorComponent> Components, IReadOnlyList<EditorBone> Bones,
    string? PrefabPath, string? Tag, string? Layer, string? Role)
{
    public static EditorObject From(EditorObjectSnapshot s) => new(s.Id, s.Name, s.Kind.ToString(),
        EditorVector.From(s.Position), EditorRotation.From(s.Rotation), EditorVector.From(s.Scale), s.IsEnabled,
        (s.Components ?? Array.Empty<Engine.Components.ComponentRecord>()).Select(c => new EditorComponent(c.Type, c.InstanceId, c.Data.Clone())).ToArray(),
        (s.Bones ?? Array.Empty<BoneSnapshot>()).Select(b => new EditorBone(b.Id, b.Name, b.ParentIndex,
            EditorVector.From(b.Position), EditorRotation.From(b.Rotation))).ToArray(),
        s.PrefabPath, s.Tag, s.Layer, s.RoleName);
}

public sealed record EditorObjectPage(EditorStatus Status, int Total, int Offset, int? NextOffset, IReadOnlyList<EditorObject> Objects);
public sealed record EditorWriteResult(string RequestId, EditorStatus Status, int? ObjectId,
    Guid? ComponentInstanceId, EditorObject? Object);
public sealed record EditorOperationStatus(string RequestId, string State, EditorWriteResult? Result, string? Error);
