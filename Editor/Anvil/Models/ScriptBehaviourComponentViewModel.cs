using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using Engine.Components;
using Engine.Scripting;

namespace Anvil.Models;

public partial class ScriptBehaviourComponentViewModel : ComponentViewModel
{
    public IReadOnlyList<ScriptDefinition> AvailableScripts { get; } = ScriptRegistry.All.ToArray();
    [ObservableProperty] private ScriptDefinition? _selectedScript;
    [ObservableProperty] private string _scriptId = string.Empty;

    /// <summary>The selected script's serialized fields ([SerializeField] / public), minus [HideInInspector] ones.</summary>
    public ObservableCollection<ScriptFieldViewModel> Fields { get; } = new();
    public bool HasFields => Fields.Count > 0;

    // Script the Fields list was built for, and the engine's stored values last seen for it.
    private string? _fieldsScriptId;
    private IReadOnlyDictionary<string, JsonElement> _stored = new Dictionary<string, JsonElement>();

    public ScriptBehaviourComponentViewModel(SceneObjectViewModel owner) : base(owner, ScriptBehaviourComponent.TypeId) { }

    partial void OnSelectedScriptChanged(ScriptDefinition? value)
    {
        // ComboBox can temporarily clear selection while its template is rebuilt.
        if (value != null) ScriptId = value.Id;
    }

    partial void OnScriptIdChanged(string value)
    {
        // Values saved for the previous script do not belong to the new one.
        Push(c =>
        {
            var script = (ScriptBehaviourComponent)c;
            if (script.ScriptId == value) return;
            script.ScriptId = value;
            script.ClearFields();
        });
        // Rebuild right away: the reconciler leaves a focused inspector alone, and the picker has focus.
        if (value != _fieldsScriptId)
        {
            _stored = new Dictionary<string, JsonElement>();
            RebuildFields(value);
        }
    }

    internal void PushField(string name, JsonElement value) =>
        Push(c => ((ScriptBehaviourComponent)c).SetFieldJson(name, value));

    internal void ResetField(string name)
    {
        Push(c => ((ScriptBehaviourComponent)c).ResetField(name));
        // Show the default immediately; the engine confirms on the next snapshot.
        var stored = new Dictionary<string, JsonElement>(_stored);
        stored.Remove(name);
        var field = Fields.FirstOrDefault(f => f.Name == name);
        if (field != null)
            foreach (string former in field.Info.FormerNames) stored.Remove(former);
        _stored = stored;
        field?.Load(_stored);
    }

    private void RebuildFields(string scriptId)
    {
        _fieldsScriptId = scriptId;
        Fields.Clear();
        foreach (var info in ScriptFields.For(scriptId))
        {
            if (info.HideInInspector) continue;
            var field = ScriptFieldViewModel.Create(this, info);
            field.Load(_stored);
            Fields.Add(field);
        }
        OnPropertyChanged(nameof(HasFields));
    }

    public override void Apply(GameComponent component, bool freezeFields)
    {
        base.Apply(component, freezeFields);
        if (freezeFields) return;
        var script = (ScriptBehaviourComponent)component;
        ScriptId = script.ScriptId;
        SelectedScript = AvailableScripts.FirstOrDefault(s => s.Id == ScriptId);
        _stored = script.Fields;
        if (_fieldsScriptId != ScriptId) RebuildFields(ScriptId);
        else foreach (var field in Fields) field.Load(_stored);
    }
}
