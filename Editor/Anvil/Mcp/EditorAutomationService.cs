using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Engine.Editor;
using ModelContextProtocol;

namespace Anvil.Mcp;

/// <summary>Owns one editor connection. DTO capture happens through the bridge, never by reading live scene lists.</summary>
public sealed class EditorAutomationService
{
    private readonly TimeSpan _commandTimeout;
    public EditorAutomationService(TimeSpan? commandTimeout = null) => _commandTimeout = commandTimeout ?? TimeSpan.FromSeconds(10);
    private sealed record Operation(string Fingerprint, Task<EditorAutomationResult> Task);
    private readonly object _gate = new();
    private readonly Dictionary<string, Operation> _operations = new(StringComparer.Ordinal);
    private readonly Queue<string> _recent = new();
    private IEditorBridge? _bridge;
    private CancellationTokenSource _lifetime = new();
    private volatile bool _enabled;
    private volatile bool _readOnly = true;
    private static readonly JsonSerializerOptions FingerprintJson = new() { IncludeFields = true };

    public string InstanceId { get; } = Guid.NewGuid().ToString("N");
    public bool ReadOnly { get => _readOnly; set => _readOnly = value; }
    public bool IsActive => _enabled;
    public bool HasBridge { get { lock (_gate) return _bridge != null; } }
    public string[] RecentOperations { get { lock (_gate) return _recent.ToArray(); } }

    public void AttachBridge(IEditorBridge bridge) { lock (_gate) _bridge = bridge; }

    public void Start()
    {
        lock (_gate)
        {
            if (_enabled) return;
            _lifetime.Dispose();
            _lifetime = new();
            _enabled = true;
        }
    }

    public void Stop(bool detach = false)
    {
        lock (_gate)
        {
            _enabled = false;
            _lifetime.Cancel();
            if (detach) _bridge = null;
        }
    }

    public EditorStatus Status(EditorAutomationState? state) => new(InstanceId, state != null, ReadOnly,
        state?.SceneGeneration, state?.Revision ?? 0, state?.CapturedAt, state?.SceneName, state?.ScenePath,
        state?.IsDirty ?? false, state?.Mode.ToString(), state?.SelectedId);

    public async Task<EditorStatus> GetStatusAsync(CancellationToken ct)
    {
        try { return Status((await CaptureAsync(ct)).State); }
        catch (McpException ex) when (ex.Message.StartsWith("EDITOR_NOT_READY:", StringComparison.Ordinal)) { return Status(null); }
    }

    public Task<EditorAutomationResult> CaptureAsync(CancellationToken ct) => RunAsync(null, new(), ct);

    public async Task<EditorWriteResult> WriteAsync(string requestId, EditorAutomationRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(requestId) || requestId.Length > 128)
            throw new McpException("INVALID_ARGUMENT: requestId must contain 1 to 128 characters. Reuse it only to retry the same command.");
        if (string.IsNullOrWhiteSpace(request.SceneGeneration))
            throw new McpException("INVALID_ARGUMENT: sceneGeneration from editor_get_status is required.");
        var result = await RunAsync(requestId, request, ct);
        return WriteResult(requestId, result);
    }

    private EditorWriteResult WriteResult(string requestId, EditorAutomationResult result)
    {
        var target = result.State.Objects.FirstOrDefault(o => o.Id == result.ObjectId);
        return new(requestId, Status(result.State), result.ObjectId, result.ComponentInstanceId,
            target.Name == null ? null : EditorObject.From(target));
    }

    public EditorOperationStatus GetOperation(string requestId)
    {
        lock (_gate)
        {
            if (!_operations.TryGetValue(requestId, out var operation))
                throw new McpException("OPERATION_NOT_FOUND: No retained command has that requestId. Do not blindly retry an unknown write.");
            if (!operation.Task.IsCompleted) return new(requestId, "pending", null, null);
            if (operation.Task.IsCompletedSuccessfully) return new(requestId, "completed", WriteResult(requestId, operation.Task.Result), null);
            return new(requestId, operation.Task.IsCanceled ? "cancelled" : "failed", null,
                operation.Task.Exception?.GetBaseException().Message ?? "Command cancelled before execution.");
        }
    }

    private async Task<EditorAutomationResult> RunAsync(string? requestId, EditorAutomationRequest request, CancellationToken ct)
    {
        Task<EditorAutomationResult> task;
        CancellationTokenSource? requestLifetime = null;
        lock (_gate)
        {
            if (!_enabled) throw new McpException("EDITOR_NOT_READY: MCP is disabled or the editor is shutting down.");
            if (_bridge == null) throw new McpException("EDITOR_NOT_READY: Waiting for the engine bridge.");
            if (requestId != null && ReadOnly) throw new McpException("READ_ONLY: Disable Read-only in the editor MCP panel to allow commands.");
            string fingerprint = JsonSerializer.Serialize(request, FingerprintJson);
            if (requestId != null && _operations.TryGetValue(requestId, out var old))
            {
                if (fingerprint != old.Fingerprint) throw new McpException("REQUEST_ID_CONFLICT: requestId was already used for different arguments.");
                task = old.Task;
            }
            else
            {
                // Retain up to 256 commands for this running editor. Never evict an in-flight command.
                if (requestId != null && _operations.Count >= 256)
                {
                    string? completed = _operations.FirstOrDefault(p => p.Value.Task.IsCompleted).Key;
                    if (completed == null) throw new McpException("QUEUE_FULL: Too many outstanding commands.");
                    _operations.Remove(completed);
                }
                requestLifetime = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, ct);
                requestLifetime.CancelAfter(_commandTimeout);
                task = _bridge.ExecuteAutomationAsync(request, requestLifetime.Token, () => _enabled && !ReadOnly);
                if (requestId != null) _operations.Add(requestId, new(fingerprint, task));
                // Observe results even when the HTTP caller disconnected or stopped waiting.
                _ = ObserveAsync(task, requestLifetime, requestId);
            }
        }
        try { return await task.WaitAsync(_commandTimeout, ct).ConfigureAwait(false); }
        catch (TimeoutException)
        {
            throw new McpException(requestId == null ? "EDITOR_NOT_READY: The engine did not respond before the timeout."
                : $"OPERATION_PENDING: Timed out waiting for {requestId}. Query editor_get_operation; a timeout does not undo a command.");
        }
        catch (EditorAutomationException ex) { throw new McpException($"{ex.Code}: {ex.Message}"); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new McpException(requestId == null ? "EDITOR_NOT_READY: The engine did not respond or MCP was stopped."
                : "COMMAND_CANCELLED: The command was cancelled before execution or MCP was stopped.");
        }
    }

    private async Task ObserveAsync(Task<EditorAutomationResult> task, CancellationTokenSource lifetime, string? requestId)
    {
        string outcome;
        try
        {
            var result = await task.ConfigureAwait(false);
            outcome = "completed";
        }
        catch (Exception ex) { outcome = ex is OperationCanceledException ? "cancelled" : ex is EditorAutomationException ae ? ae.Code : "failed"; }
        finally { lifetime.Dispose(); }
        if (requestId != null)
            lock (_gate)
            {
                // Do not log untrusted arguments, access tokens, or request IDs.
                _recent.Enqueue($"{DateTime.Now:HH:mm:ss} Command {outcome}");
                while (_recent.Count > 20) _recent.Dequeue();
            }
    }
}
