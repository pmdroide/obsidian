using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Engine.Components;

namespace Anvil.Models;

public partial class InteractableComponentViewModel : ComponentViewModel
{
    [ObservableProperty] private string _prompt = "Interact";
    [ObservableProperty] private double _range = InteractableComponent.DefaultRange;

    public InteractableComponentViewModel(SceneObjectViewModel owner) : base(owner, InteractableComponent.TypeId) { }

    partial void OnPromptChanged(string value)
    {
        string prompt = value?.Trim() ?? "";
        Push(c => ((InteractableComponent)c).Prompt = prompt);
    }

    partial void OnRangeChanged(double value)
    {
        if (!double.IsFinite(value)) return;
        float range = (float)Math.Max(value, 0);
        Push(c => ((InteractableComponent)c).Range = range);
    }

    public override void Apply(GameComponent component, bool freezeFields)
    {
        base.Apply(component, freezeFields);
        if (freezeFields) return;
        var interactable = (InteractableComponent)component;
        Prompt = interactable.Prompt ?? "";
        if (Math.Abs(Range - interactable.Range) > 0.0001) Range = interactable.Range;
    }
}
