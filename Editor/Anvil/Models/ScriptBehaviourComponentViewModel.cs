using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Engine.Components;
using Engine.Scripting;

namespace Anvil.Models;

public partial class ScriptBehaviourComponentViewModel : ComponentViewModel
{
    public IReadOnlyList<ScriptDefinition> AvailableScripts { get; } = ScriptRegistry.All.ToArray();
    [ObservableProperty] private ScriptDefinition? _selectedScript;
    [ObservableProperty] private string _scriptId = string.Empty;

    public ScriptBehaviourComponentViewModel(SceneObjectViewModel owner) : base(owner, ScriptBehaviourComponent.TypeId) { }

    partial void OnSelectedScriptChanged(ScriptDefinition? value)
    {
        // ComboBox can temporarily clear selection while its template is rebuilt.
        if (value != null) ScriptId = value.Id;
    }

    partial void OnScriptIdChanged(string value) => Push(c => ((ScriptBehaviourComponent)c).ScriptId = value);

    public override void Apply(GameComponent component, bool freezeFields)
    {
        base.Apply(component, freezeFields);
        if (freezeFields) return;
        ScriptId = ((ScriptBehaviourComponent)component).ScriptId;
        SelectedScript = AvailableScripts.FirstOrDefault(s => s.Id == ScriptId);
    }
}
