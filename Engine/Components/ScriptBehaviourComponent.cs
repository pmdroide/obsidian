using System.Text.Json;
using System.Text.Json.Serialization;
using Engine.Editor;
using Engine.Entities;
using Engine.Scripting;
using Microsoft.Xna.Framework;

namespace Engine.Components;

/// <summary>
/// Persisted script selection with per-owner, Play-only runtime behaviour. Hosted by a
/// gameobject, or by the main camera (<see cref="HostCamera"/>, hooks get a null owner).
/// </summary>
public sealed class ScriptBehaviourComponent : GameComponent
{
    public const string TypeId = "script-behaviour";
    public string ScriptId { get; set; } = SpinExampleScript.ScriptId;

    private Dictionary<string, JsonElement> _fields = new();
    /// <summary>
    /// Serialized field values of this attachment, by field name (see <see cref="ScriptFields"/>).
    /// Fields without an entry keep the script's C# initializer. Change values with
    /// <see cref="SetField"/> / <see cref="ResetField"/> so a running script picks them up.
    /// </summary>
    public Dictionary<string, JsonElement> Fields
    {
        get => _fields;
        set
        {
            _fields = value ?? new Dictionary<string, JsonElement>();
            _dirtyFields.Clear();
            _reapplyAllFields = true;
        }
    }

    // Edits made while the script runs are written into the live instance on its next frame.
    private readonly HashSet<string> _dirtyFields = new();
    private bool _reapplyAllFields;

    private ScriptBehaviour _instance;
    private string _activeScriptId;
    private bool _attemptedStart;
    private bool _faulted;
    [JsonIgnore] public bool IsRunning => _instance != null && !_faulted;
    [JsonIgnore] public string LastError { get; private set; }
    /// <summary>The running script keeps updating while the game is paused (<see cref="ScriptBehaviour.UpdateWhilePaused"/>).</summary>
    [JsonIgnore] public bool UpdatesWhilePaused => IsRunning && _instance.UpdateWhilePaused;
    /// <summary>Set by <see cref="Camera.AddComponent"/> when this script runs on the main camera.</summary>
    [JsonIgnore] public Camera HostCamera { get; internal set; }

    private bool HostEnabled(BasicEntity owner) => owner?.IsEnabled ?? HostCamera != null;

    public override void OnStart(BasicEntity owner)
    {
        OnStop();
        EnsureStarted(owner);
    }

    private void EnsureStarted(BasicEntity owner)
    {
        if (!Enabled || !HostEnabled(owner) || _attemptedStart) return;
        _attemptedStart = true;
        _faulted = false;
        _activeScriptId = ScriptId;
        LastError = null;
        if (string.IsNullOrWhiteSpace(ScriptId)) return;
        try
        {
            var definition = ScriptRegistry.Find(ScriptId)
                ?? throw new InvalidOperationException($"Script '{ScriptId}' is not registered.");
            _instance = definition.Create();
            // Serialized values are in place before Start, like Unity.
            ScriptFields.Apply(_instance, _fields, warn: EditorBridge.Log);
            _dirtyFields.Clear();
            _reapplyAllFields = false;
            _instance.Attach(owner, owner == null ? HostCamera : null);
            Run(s => s.Start());
        }
        catch (Exception ex) { Fail("Start", ex); }
    }

    public override void OnUpdate(BasicEntity owner, GameTime time)
    {
        OnChanged(owner);
        // Also handles attachment or re-enabling during Play, before the first Update.
        EnsureStarted(owner);
        if (!IsRunning) return;
        ApplyFieldEdits();
        // Inline rather than Run(...), so the per-frame call doesn't allocate a closure.
        ScriptBehaviour previous = ScriptBehaviour.Running;
        ScriptBehaviour.Running = _instance;
        try { _instance.Tick((float)time.ElapsedGameTime.TotalSeconds); }
        catch (Exception ex) { Fail("Update", ex); }
        finally { ScriptBehaviour.Running = previous; }
    }

    /// <summary>
    /// Runs an event hook (collision, trigger, interaction) on the running behaviour. A throwing
    /// hook stops the script, like a throwing Update.
    /// </summary>
    internal void Notify(string hook, Action<ScriptBehaviour> call)
    {
        if (!IsRunning) return;
        try { Run(call); }
        catch (Exception ex) { Fail(hook, ex); }
    }

    /// <summary>Calls into the behaviour with <see cref="ScriptBehaviour.Running"/> set (hooks can nest, e.g. Interact).</summary>
    private void Run(Action<ScriptBehaviour> call)
    {
        ScriptBehaviour previous = ScriptBehaviour.Running;
        ScriptBehaviour.Running = _instance;
        try { call(_instance); }
        finally { ScriptBehaviour.Running = previous; }
    }

    /// <summary>
    /// The value this attachment gives a serialized field: stored, otherwise the script's default.
    /// A GameObject field gives the gameobject in the active scene (null when none or gone).
    /// </summary>
    public T GetField<T>(string name)
    {
        var field = RequireField(name);
        return (T)ScriptFields.ToFieldValue(field, ScriptFields.GetValue(field, _fields));
    }

    /// <summary>
    /// Stores a serialized field value (any compatible type, e.g. an int for a float field). A running
    /// script receives it before its next Update. Throws for an unknown field or an incompatible value.
    /// </summary>
    public void SetField(string name, object value) => SetFieldJson(name, ScriptFields.Encode(RequireField(name), value));

    /// <summary>Stores an already encoded value (the editor's path). Values that do not decode are rejected.</summary>
    public void SetFieldJson(string name, JsonElement value)
    {
        var field = RequireField(name);
        if (!ScriptFields.TryDecode(field, value, out _))
            throw new ArgumentException($"{value.GetRawText()} is not a valid {field.FieldType.Name} for field '{name}'.", nameof(value));
        foreach (string former in field.FormerNames) _fields.Remove(former);
        _fields[name] = value.Clone();
        _dirtyFields.Add(name);
    }

    /// <summary>Removes the stored value, so the field uses the script's C# initializer again.</summary>
    public void ResetField(string name)
    {
        var field = RequireField(name);
        bool removed = _fields.Remove(name);
        foreach (string former in field.FormerNames) removed |= _fields.Remove(former);
        if (removed) _dirtyFields.Add(name);
    }

    /// <summary>
    /// Points GameObject fields that store <paramref name="fromId"/> at <paramref name="toId"/>, so a
    /// duplicated gameobject's references to itself follow the copy.
    /// </summary>
    internal void RemapGameObjectReferences(int fromId, int toId)
    {
        foreach (var field in ScriptFields.For(ScriptId))
        {
            if (field.Kind != ScriptFieldKind.GameObject || !ScriptFields.TryGetStored(field, _fields, out var json) ||
                !ScriptFields.TryDecode(field, json, out var id) || (int)id != fromId) continue;
            SetFieldJson(field.Name, ScriptFields.Encode(field, toId));
        }
    }

    /// <summary>Drops every stored value, e.g. after switching to another script.</summary>
    public void ClearFields()
    {
        if (_fields.Count == 0) return;
        _fields.Clear();
        _reapplyAllFields = true;
    }

    private ScriptFieldInfo RequireField(string name) =>
        ScriptFields.Find(ScriptId, name)
        ?? throw new ArgumentException($"Script '{ScriptId}' has no serialized field '{name}'.", nameof(name));

    private void ApplyFieldEdits()
    {
        if (!_reapplyAllFields && _dirtyFields.Count == 0) return;
        try
        {
            if (_reapplyAllFields)
                ScriptFields.Apply(_instance, _fields, ScriptFields.For(_instance.GetType()).Select(f => f.Name).ToArray(), EditorBridge.Log);
            else
                ScriptFields.Apply(_instance, _fields, _dirtyFields, EditorBridge.Log);
        }
        finally
        {
            _dirtyFields.Clear();
            _reapplyAllFields = false;
        }
    }

    public override void OnChanged(BasicEntity owner)
    {
        if (!Enabled || !HostEnabled(owner) || (_attemptedStart && _activeScriptId != ScriptId)) OnStop();
    }

    public override void OnStop()
    {
        var instance = _instance;
        _instance = null;
        _attemptedStart = false;
        _faulted = false;
        _activeScriptId = null;
        if (instance == null) return;
        try { instance.Shutdown(); }
        catch (Exception ex) { Fail("Stop", ex); }
    }

    private void Fail(string hook, Exception ex)
    {
        _faulted = true;
        LastError = $"Script '{ScriptId}' {hook} failed: {ex.Message}";
        EditorBridge.Log(LastError + Environment.NewLine + ex);
    }
}
