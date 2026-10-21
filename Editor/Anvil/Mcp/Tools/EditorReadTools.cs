using System;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Engine.Components;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Anvil.Mcp.Tools;

[McpServerToolType]
public sealed class EditorReadTools(EditorAutomationService editor)
{
    [McpServerTool(Name = "editor_get_status", ReadOnly = true, Destructive = false, OpenWorld = false, UseStructuredContent = true)]
    [Description("Inspect this running Anvil instance, readiness, sceneGeneration, mode, dirty state and selection. Use sceneGeneration for subsequent writes.")]
    public Task<EditorStatus> Status(CancellationToken cancellationToken) => editor.GetStatusAsync(cancellationToken);

    [McpServerTool(Name = "scene_list_objects", ReadOnly = true, Destructive = false, OpenWorld = false, UseStructuredContent = true)]
    [Description("List scene objects with transforms and components. Positions use engine units with Z up; rotations are quaternions. Filter names with contains; page with offset and limit (1-100).")]
    public async Task<EditorObjectPage> List(CancellationToken cancellationToken, string? contains = null, string? kind = null, int offset = 0, int limit = 50)
    {
        if (offset < 0 || limit is < 1 or > 100 || contains?.Length > 256)
            throw new McpException("INVALID_ARGUMENT: offset must be non-negative, limit 1-100, and contains at most 256 characters.");
        if (kind != null && !Enum.TryParse<Engine.Editor.EditorObjectKind>(kind, true, out _))
            throw new McpException("INVALID_ARGUMENT: Unknown object kind.");
        var state = (await editor.CaptureAsync(cancellationToken)).State;
        var matches = state.Objects.Where(o => (contains == null || o.Name.Contains(contains, StringComparison.OrdinalIgnoreCase)) &&
            (kind == null || o.Kind.ToString().Equals(kind, StringComparison.OrdinalIgnoreCase))).ToArray();
        var objects = matches.Skip(offset).Take(limit).Select(EditorObject.From).ToArray();
        int next = offset + objects.Length;
        return new(editor.Status(state), matches.Length, offset, next < matches.Length ? next : null, objects);
    }

    [McpServerTool(Name = "scene_get_object", ReadOnly = true, Destructive = false, OpenWorld = false, UseStructuredContent = true)]
    [Description("Read one object with components and bones from a fresh game-thread capture. Bone positions are world-space; bone rotations are local offsets from rest pose.")]
    public async Task<EditorObject> Get(int objectId, CancellationToken cancellationToken)
    {
        var state = (await editor.CaptureAsync(cancellationToken)).State;
        var match = state.Objects.FirstOrDefault(o => o.Id == objectId);
        if (match.Name == null) throw new McpException("OBJECT_NOT_FOUND: No object has that ID in the current scene. Bones are listed under their owning object.");
        return EditorObject.From(match);
    }

    [McpServerTool(Name = "assets_list_models", ReadOnly = true, Destructive = false, OpenWorld = false, UseStructuredContent = true)]
    [Description("List available model keys for scene_create_object. Supports case-insensitive filtering and pages of at most 100 keys.")]
    public async Task<object> Models(CancellationToken cancellationToken, string? contains = null, int offset = 0, int limit = 100)
    {
        if (offset < 0 || limit is < 1 or > 100 || contains?.Length > 256) throw new McpException("INVALID_ARGUMENT: Invalid filter or pagination.");
        var state = (await editor.CaptureAsync(cancellationToken)).State;
        var keys = state.ModelKeys.Where(k => contains == null || k.Contains(contains, StringComparison.OrdinalIgnoreCase)).ToArray();
        var page = keys.Skip(offset).Take(limit).ToArray();
        return new { status = editor.Status(state), total = keys.Length, offset, nextOffset = offset + page.Length < keys.Length ? (int?)(offset + page.Length) : null, modelKeys = page };
    }

    [McpServerTool(Name = "components_list_types", ReadOnly = true, Destructive = false, OpenWorld = false, UseStructuredContent = true)]
    [Description("List registered component type IDs, display names, multiplicity, and main-camera support for scene_add_component.")]
    public object Components() => new { types = ComponentRegistry.All.Select(d => new { id = d.Id, name = d.DisplayName, d.AllowMultiple, cameraSupported = Engine.Entities.Camera.SupportsComponent(d.Id) }).ToArray() };

    [McpServerTool(Name = "editor_get_operation", ReadOnly = true, Destructive = false, OpenWorld = false, UseStructuredContent = true)]
    [Description("Check a command by requestId after a timeout or disconnect. Retains up to 256 commands per editor instance; pending commands are never evicted.")]
    public EditorOperationStatus Operation(string requestId) => editor.GetOperation(requestId);
}
