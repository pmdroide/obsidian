using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Engine.Components;
using Engine.Entities;
using Engine.Logic;
using Engine.Recources;
using Microsoft.Xna.Framework;

namespace Engine.Editor;

public enum EditorAutomationCommand { Capture, Select, SetTransform, CreateObject, AddComponent, Save, SetPlayMode }

/// <summary>Engine-side contract. No dependency on MCP or the editor HTTP host.</summary>
public sealed record EditorAutomationRequest
{
    public EditorAutomationCommand Command { get; init; }
    public string SceneGeneration { get; init; }
    public int? ObjectId { get; init; }
    public Vector3? Position { get; init; }
    public Vector3? Scale { get; init; }
    public Quaternion? Rotation { get; init; }
    public string ModelKey { get; init; }
    public string Name { get; init; }
    public string ComponentType { get; init; }
    public string Path { get; init; }
    public bool Play { get; init; }
}

public sealed record EditorAutomationState(string SceneGeneration, long Revision, DateTimeOffset CapturedAt,
    string SceneName, string ScenePath, bool IsDirty, GameMode Mode, int? SelectedId,
    IReadOnlyList<EditorObjectSnapshot> Objects, IReadOnlyList<string> ModelKeys);

public sealed record EditorAutomationResult(EditorAutomationState State, int? ObjectId = null, Guid? ComponentInstanceId = null);

public sealed class EditorAutomationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed partial class EditorBridge
{
    private sealed class AutomationWork(EditorAutomationRequest request, CancellationToken cancellation,
        Func<bool> allowWrite, TaskCompletionSource<EditorAutomationResult> completion)
    {
        public EditorAutomationRequest Request { get; } = request;
        public Func<bool> AllowWrite { get; } = allowWrite;
        public TaskCompletionSource<EditorAutomationResult> Completion { get; } = completion;
        public CancellationTokenRegistration Registration;
        private int _state; // 0 queued, 1 running, 2 cancelled before execution
        public bool TryStart() => Interlocked.CompareExchange(ref _state, 1, 0) == 0;
        public void Cancel()
        {
            if (Interlocked.CompareExchange(ref _state, 2, 0) == 0) Completion.TrySetCanceled(cancellation);
        }
    }

    private readonly ConcurrentQueue<AutomationWork> _automationQueue = new();
    private readonly object _automationGate = new();
    private int _automationPending;
    private string _automationSceneGeneration = Guid.NewGuid().ToString("N");
    private long _automationRevision;
    private volatile bool _automationReady;
    internal string AutomationContentRootOverride { get; set; }

    public Task<EditorAutomationResult> ExecuteAutomationAsync(EditorAutomationRequest request,
        CancellationToken cancellationToken = default, Func<bool> allowWrite = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_automationGate)
        {
            if (!_automationReady)
                return Task.FromException<EditorAutomationResult>(Error("EDITOR_NOT_READY", "The engine is initializing or has stopped."));
            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled<EditorAutomationResult>(cancellationToken);
            if (Interlocked.Increment(ref _automationPending) > 64)
            {
                Interlocked.Decrement(ref _automationPending);
                return Task.FromException<EditorAutomationResult>(Error("QUEUE_FULL", "The editor automation queue is full. Retry later."));
            }
            var completion = new TaskCompletionSource<EditorAutomationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var work = new AutomationWork(request, cancellationToken, allowWrite, completion);
            work.Registration = cancellationToken.Register(work.Cancel);
            _automationQueue.Enqueue(work);
            return completion.Task;
        }
    }

    /// <summary>Called before the host tears down its engine thread.</summary>
    public void StopAutomation()
    {
        lock (_automationGate)
        {
            _automationReady = false;
            while (_automationQueue.TryDequeue(out var work))
            {
                Interlocked.Decrement(ref _automationPending);
                work.Cancel();
                work.Registration.Dispose();
            }
        }
    }

    // Separate budget from UI commands: automation cannot consume an unbounded number of operations in a frame.
    private void DrainAutomation()
    {
        for (int i = 0; i < 4 && _automationQueue.TryDequeue(out var work); i++)
        {
            Interlocked.Decrement(ref _automationPending);
            if (!work.TryStart())
            {
                work.Registration.Dispose();
                continue;
            }
            try
            {
                if (work.Request.Command != EditorAutomationCommand.Capture && work.AllowWrite?.Invoke() == false)
                    throw Error("READ_ONLY", "Editor automation is read-only or has been disabled.");
                work.Completion.TrySetResult(ExecuteAutomation(work.Request));
            }
            catch (Exception ex) { work.Completion.TrySetException(ex); }
            finally { work.Registration.Dispose(); }
        }
    }

    private EditorAutomationResult ExecuteAutomation(EditorAutomationRequest request)
    {
        if (_scene?.ActiveScene == null || _editor == null)
            throw Error("EDITOR_NOT_READY", "The engine has not finished initializing a scene.");
        bool write = request.Command != EditorAutomationCommand.Capture;
        if (write && request.SceneGeneration != _automationSceneGeneration)
            throw Error("SCENE_CHANGED", "Read editor_get_status again and use its sceneGeneration.");
        if (write && request.Command != EditorAutomationCommand.SetPlayMode && Mode != GameMode.Edit)
            throw Error("WRONG_MODE", "Stop Play before editing or saving the scene.");
        ValidateTransform(request);
        int? objectId = request.ObjectId;
        Guid? componentId = null;
        switch (request.Command)
        {
            case EditorAutomationCommand.Capture: break;
            case EditorAutomationCommand.Select:
                // The main camera has no TransformableObject proxy in the current editor.
                _editor.SelectedObject = request.ObjectId is int selected ? AutomationObject(selected) : null;
                break;
            case EditorAutomationCommand.SetTransform:
                if (request.ObjectId == null) throw Error("INVALID_ARGUMENT", "objectId is required.");
                if (request.Position == null && request.Scale == null && request.Rotation == null)
                    throw Error("INVALID_ARGUMENT", "Provide position, scale or rotation.");
                var target = AutomationObject(request.ObjectId.Value);
                if (target is BoneObject && (request.Position != null || request.Scale != null))
                    throw Error("INVALID_ARGUMENT", "Bones support rotation only, in bone-local space.");
                if (target is not BasicEntity && target is not Decal && request.Scale != null)
                    throw Error("INVALID_ARGUMENT", "Scale is supported for gameobjects and decals only.");
                if (request.Position is { } position) target.Position = position;
                if (request.Scale is { } scale) target.Scale = scale;
                if (request.Rotation is { } rotation) target.RotationMatrix = Matrix.CreateFromQuaternion(Quaternion.Normalize(rotation));
                _scene.ActiveScene.IsDirty = true;
                break;
            case EditorAutomationCommand.CreateObject:
                if (request.Name?.Length > 256) throw Error("INVALID_ARGUMENT", "name must be at most 256 characters.");
                if (string.IsNullOrWhiteSpace(request.ModelKey) || !_modelKeys.TryGetValue(request.ModelKey, out var model))
                    throw Error("INVALID_ARGUMENT", "Unknown modelKey. Use assets_list_models.");
                var created = AddBasicEntityNow(request.ModelKey, model, request.Position ?? Vector3.Zero,
                    request.Name, request.Scale, null);
                if (created == null) throw Error("CREATE_FAILED", "The engine did not create the gameobject.");
                if (request.Rotation is { } createRotation)
                    created.RotationMatrix = Matrix.CreateFromQuaternion(Quaternion.Normalize(createRotation));
                _scene.ActiveScene.IsDirty = true;
                objectId = created.Id;
                break;
            case EditorAutomationCommand.AddComponent:
                var definition = ComponentRegistry.Find(request.ComponentType)
                    ?? throw Error("INVALID_ARGUMENT", "Unknown componentType. Use components_list_types.");
                var ownerId = request.ObjectId ?? throw Error("INVALID_ARGUMENT", "objectId is required.");
                var owner = ownerId == MainCameraId ? null : AutomationObject(ownerId) as BasicEntity;
                if (ownerId != MainCameraId && owner == null)
                    throw Error("INVALID_ARGUMENT", "Components require a gameobject or the main camera.");
                if (ownerId == MainCameraId && (_scene.ActiveScene.MainCamera == null || !Camera.SupportsComponent(definition.Id)))
                    throw Error("INVALID_ARGUMENT", "The main camera accepts Script Behaviour components only.");
                var attachments = ownerId == MainCameraId ? _scene.ActiveScene.MainCamera.Components : owner.Components;
                if (!definition.AllowMultiple && attachments.Any(c => c.GetType() == definition.ComponentType))
                    throw Error("COMPONENT_EXISTS", "This object already has that component.");
                var component = definition.Create(owner);
                if (ownerId == MainCameraId && component is ScriptBehaviourComponent script)
                    script.ScriptId = Scripting.FreecamScript.ScriptId;
                bool added = ownerId == MainCameraId ? _scene.ActiveScene.MainCamera.AddComponent(component) : owner.AddComponent(component);
                if (!added) throw Error("ADD_COMPONENT_FAILED", "The engine rejected the component.");
                _scene.ActiveScene.IsDirty = true;
                componentId = component.InstanceId;
                break;
            case EditorAutomationCommand.Save:
                string savePath = ResolveAutomationScenePath(request.Path ?? CurrentScenePath);
                if (!_scene.SceneManager.SaveScene(savePath)) throw Error("SAVE_FAILED", "The scene could not be saved. Check the editor console.");
                break;
            case EditorAutomationCommand.SetPlayMode:
                if (_scene.PlayMode == null) throw Error("EDITOR_NOT_READY", "Play mode is not initialized.");
                if (request.Play) _scene.PlayMode.Play(); else _scene.PlayMode.Stop();
                if ((Mode == GameMode.Play) != request.Play) throw Error("MODE_CHANGE_FAILED", "The engine did not enter the requested mode.");
                break;
            default: throw Error("INVALID_ARGUMENT", "Unsupported editor command.");
        }
        // Capture now, not from the throttled UI snapshot. Everything here still runs on the game thread.
        var capturedObjects = BuildSnapshot();
        // Retried writes retain only their target, not 256 copies of the entire scene and model catalog.
        var resultObjects = write ? capturedObjects.Where(o => o.Id == objectId).ToArray() : capturedObjects.ToArray();
        var state = new EditorAutomationState(_automationSceneGeneration, ++_automationRevision, DateTimeOffset.UtcNow,
            CurrentSceneName, CurrentScenePath, IsSceneDirty, Mode, _editor.SelectedObject?.Id,
            resultObjects, write ? Array.Empty<string>() : _modelKeyList.ToArray());
        return new(state, objectId, componentId);
    }

    private TransformableObject AutomationObject(int id)
    {
        if (id == MainCameraId) throw Error("INVALID_ARGUMENT", "The main camera supports inspection and components; selection/transform tools require another object.");
        if (id < MainCameraId && !BuildSnapshot().Any(s => s.Bones?.Any(b => b.Id == id) == true))
            throw Error("OBJECT_NOT_FOUND", "The bone no longer exists in this scene.");
        return LookupById(id) ?? throw Error("OBJECT_NOT_FOUND", "The object no longer exists in this scene.");
    }

    private static void ValidateTransform(EditorAutomationRequest request)
    {
        static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z)
            && Math.Abs(v.X) <= 1e6f && Math.Abs(v.Y) <= 1e6f && Math.Abs(v.Z) <= 1e6f;
        if (request.Position is { } p && !Finite(p)) throw Error("INVALID_ARGUMENT", "position must contain finite values within +/-1000000.");
        if (request.Scale is { } s && (!Finite(s) || Math.Abs(s.X) < 1e-5f || Math.Abs(s.Y) < 1e-5f || Math.Abs(s.Z) < 1e-5f))
            throw Error("INVALID_ARGUMENT", "scale must contain finite, non-zero factors within +/-1000000.");
        if (request.Rotation is { } q && (!float.IsFinite(q.LengthSquared()) || q.LengthSquared() < 1e-10f))
            throw Error("INVALID_ARGUMENT", "rotation must be a finite, non-zero quaternion.");
    }

    // Resolve against source Content, not Anvil's engine-bin working directory. Reject reparse points throughout
    // the existing path, including the root, so junctions cannot move writes outside the allowed scene folder.
    public static string ValidateAutomationScenePath(string contentRoot, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw Error("INVALID_ARGUMENT", "An unsaved scene requires a Save As path, such as Scenes/MyScene.obsc.");
        try { return ValidateScenePathCore(contentRoot, path); }
        catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is IOException || ex is UnauthorizedAccessException)
        {
            throw Error("INVALID_ARGUMENT", "The scene path is invalid or cannot be checked. Use a regular file under Content/Scenes.");
        }
    }

    private static string ValidateScenePathCore(string contentRoot, string path)
    {
        string root = System.IO.Path.GetFullPath(System.IO.Path.Combine(contentRoot, "Scenes"));
        string full = System.IO.Path.GetFullPath(System.IO.Path.IsPathRooted(path) ? path : System.IO.Path.Combine(contentRoot, path));
        if (!full.StartsWith(root + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(System.IO.Path.GetExtension(full), ".obsc", StringComparison.OrdinalIgnoreCase))
            throw Error("INVALID_ARGUMENT", "Scene files must be .obsc files inside the source Content/Scenes folder.");
        string relative = System.IO.Path.GetRelativePath(root, full);
        if (relative.Split(System.IO.Path.DirectorySeparatorChar).Any(part =>
            part.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0 || part != part.TrimEnd(' ', '.')))
            throw Error("INVALID_ARGUMENT", "Scene paths cannot contain alternate data streams, wildcards or trailing dots/spaces.");
        foreach (string candidate in new[] { full, System.IO.Path.ChangeExtension(full, ".probes"), System.IO.Path.ChangeExtension(full, ".reflections") })
            for (string current = candidate; current != null; current = System.IO.Path.GetDirectoryName(current))
            {
                try
                {
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                        throw Error("INVALID_ARGUMENT", "Scene/sidecar paths through symbolic links or junctions are not supported.");
                }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
            }
        return full;
    }

    private string ResolveAutomationScenePath(string path) => ValidateAutomationScenePath(AutomationContentRootOverride ?? ContentSourceRoot, path);
    private static EditorAutomationException Error(string code, string message) => new(code, message);
}
