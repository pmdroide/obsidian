using System;
using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Engine.Editor;
using Engine.Recources;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Anvil.Mcp.Tools;

[McpServerToolType]
public sealed class EditorWriteTools(EditorAutomationService editor)
{
    [McpServerTool(Name = "scene_select_object", Destructive = false, OpenWorld = false, UseStructuredContent = true)]
    [Description("Select a gameobject, light, decal or bone in Edit mode; omit objectId to clear. Main camera selection is unsupported. Requires sceneGeneration and a unique requestId.")]
    public Task<EditorWriteResult> Select(string requestId, string sceneGeneration, CancellationToken cancellationToken, int? objectId = null) =>
        editor.WriteAsync(requestId, new() { Command = EditorAutomationCommand.Select, SceneGeneration = sceneGeneration, ObjectId = objectId }, cancellationToken);

    [McpServerTool(Name = "scene_set_transform", Destructive = false, OpenWorld = false, UseStructuredContent = true)]
    [Description("Patch a transform in Edit mode, leaving omitted fields unchanged. Position is world-space (Z up); rotation is quaternion x/y/z/w. Scale applies to gameobjects/decals. Bones accept local rotation only. Main camera is unsupported. Marks the scene dirty; does not save. Reuse requestId only for an identical retry.")]
    public Task<EditorWriteResult> Transform(string requestId, string sceneGeneration, int objectId, CancellationToken cancellationToken,
        EditorVector? position = null, EditorRotation? rotation = null, EditorVector? scale = null) =>
        editor.WriteAsync(requestId, new() { Command = EditorAutomationCommand.SetTransform, SceneGeneration = sceneGeneration,
            ObjectId = objectId, Position = position?.ToEngine(), Rotation = rotation?.ToEngine(), Scale = scale?.ToEngine() }, cancellationToken);

    [McpServerTool(Name = "scene_create_object", Destructive = false, OpenWorld = false, UseStructuredContent = true)]
    [Description("Create one gameobject in Edit mode using a modelKey from assets_list_models. Defaults to origin and unit scale. Returns the created ID, marks the scene dirty, and does not save. Use a unique requestId; identical retries return the retained result.")]
    public Task<EditorWriteResult> Create(string requestId, string sceneGeneration, string modelKey, CancellationToken cancellationToken,
        string? name = null, EditorVector? position = null, EditorRotation? rotation = null, EditorVector? scale = null) =>
        editor.WriteAsync(requestId, new() { Command = EditorAutomationCommand.CreateObject, SceneGeneration = sceneGeneration,
            ModelKey = modelKey, Name = name, Position = position?.ToEngine(), Rotation = rotation?.ToEngine(), Scale = scale?.ToEngine() }, cancellationToken);

    [McpServerTool(Name = "scene_add_component", Destructive = false, OpenWorld = false, UseStructuredContent = true)]
    [Description("Attach a component using its registered componentType ID from components_list_types. Edit mode only. Returns the attachment instance ID and marks dirty. The main camera (-1) accepts Script Behaviour only. Duplicate non-repeatable types return an error.")]
    public Task<EditorWriteResult> Component(string requestId, string sceneGeneration, int objectId, string componentType, CancellationToken cancellationToken) =>
        editor.WriteAsync(requestId, new() { Command = EditorAutomationCommand.AddComponent, SceneGeneration = sceneGeneration,
            ObjectId = objectId, ComponentType = componentType }, cancellationToken);

    [McpServerTool(Name = "scene_save", Destructive = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Save the current edited scene, overwriting its file and lighting sidecars. An unsaved scene needs path, e.g. Scenes/MyScene.obsc. Save As must remain under source Content/Scenes. Edit mode only; requires writes enabled.")]
    public Task<EditorWriteResult> Save(string requestId, string sceneGeneration, CancellationToken cancellationToken, string? path = null) =>
        editor.WriteAsync(requestId, new() { Command = EditorAutomationCommand.Save, SceneGeneration = sceneGeneration, Path = path }, cancellationToken);

    [McpServerTool(Name = "editor_set_play_mode", Destructive = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Set mode to play or stop and wait for the transition. Play runs scene scripts; Stop restores the edited scene and discards runtime changes. Requires writes enabled and the current sceneGeneration.")]
    public Task<EditorWriteResult> Play(string requestId, string sceneGeneration, string mode, CancellationToken cancellationToken)
    {
        if (mode != "play" && mode != "stop") throw new McpException("INVALID_ARGUMENT: mode must be play or stop.");
        return editor.WriteAsync(requestId, new() { Command = EditorAutomationCommand.SetPlayMode, SceneGeneration = sceneGeneration, Play = mode == "play" }, cancellationToken);
    }

    [McpServerTool(Name = "materials_generate", Destructive = false, OpenWorld = false, UseStructuredContent = true)]
    [Description("Generate a tileable PBR material (Source 2 style) from a text prompt with the local texgen tool and import it as Materials/Generated/<name>.mat. Returns at once with state pending; poll editor_get_operation with the same requestId until completed (result.material.materialPath, style warnings) or failed. Omit seed for a stable one derived from requestId; family is auto or stone, brick, concrete, tile, plaster, wood, metal, painted_metal, fabric, ground, organic. backend: fake, sdxl_classic or sdxl_chord (default from texgen.json; unavailable ones fail with the reason). resolution is a power of two from 64 to 2048. assignToId links the material to that gameobject. Identical retries rejoin the same job. Requires writes enabled.")]
    public EditorOperationStatus GenerateMaterial(string requestId, string prompt, int? seed = null, string? family = null, string? backend = null,
        int resolution = 1024, string? negativePrompt = null, bool forceDielectric = false, int? assignToId = null)
    {
        if (string.IsNullOrWhiteSpace(prompt) || prompt.Length > 500 || negativePrompt?.Length > 500)
            throw new McpException("INVALID_ARGUMENT: prompt must contain 1 to 500 characters (negativePrompt at most 500).");
        if (resolution < 64 || resolution > 2048 || (resolution & (resolution - 1)) != 0)
            throw new McpException("INVALID_ARGUMENT: resolution must be a power of two from 64 to 2048.");
        if (backend == "classic") throw new McpException("INVALID_ARGUMENT: the classic backend needs a photo; use fake, sdxl_classic or sdxl_chord.");
        if (family != null && family != "auto" && !System.Text.RegularExpressions.Regex.IsMatch(family, "^[a-z_]{1,32}$"))
            throw new McpException("INVALID_ARGUMENT: family must be auto or one of the families texgen lists.");
        // Without a seed, the same requestId always means the same material.
        int chosenSeed = seed ?? (BitConverter.ToInt32(SHA256.HashData(Encoding.UTF8.GetBytes(requestId ?? "")), 0) & 0x7fffffff);
        var request = new TexGenParams
        {
            Prompt = prompt.Trim(), NegativePrompt = negativePrompt?.Trim() ?? "", Seed = chosenSeed, Resolution = resolution,
            Backend = backend ?? editor.DefaultMaterialBackend, Family = family ?? "auto", ForceDielectric = forceDielectric,
        };
        return editor.StartMaterialGeneration(requestId!, request, assignToId);
    }
}
