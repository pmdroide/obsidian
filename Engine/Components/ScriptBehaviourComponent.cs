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

    private ScriptBehaviour _instance;
    private string _activeScriptId;
    private bool _attemptedStart;
    private bool _faulted;
    [JsonIgnore] public bool IsRunning => _instance != null && !_faulted;
    [JsonIgnore] public string LastError { get; private set; }
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
            _instance.Attach(owner, owner == null ? HostCamera : null);
            _instance.Start();
        }
        catch (Exception ex) { Fail("Start", ex); }
    }

    public override void OnUpdate(BasicEntity owner, GameTime time)
    {
        OnChanged(owner);
        // Also handles attachment or re-enabling during Play, before the first Update.
        EnsureStarted(owner);
        if (!IsRunning) return;
        try { _instance.Tick((float)time.ElapsedGameTime.TotalSeconds); }
        catch (Exception ex) { Fail("Update", ex); }
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
