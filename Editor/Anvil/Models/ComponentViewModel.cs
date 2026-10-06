using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Engine.Components;

namespace Anvil.Models;

public abstract partial class ComponentViewModel : ObservableObject
{
    protected SceneObjectViewModel Owner { get; }
    public string TypeId { get; }
    public Guid InstanceId { get; private set; }
    public string DisplayName => ComponentRegistry.Find(TypeId).DisplayName;
    [ObservableProperty] private bool _enabled = true;
    public IRelayCommand RemoveCommand { get; }

    protected ComponentViewModel(SceneObjectViewModel owner, string typeId)
    {
        Owner = owner;
        TypeId = typeId;
        RemoveCommand = new RelayCommand(() =>
        {
            if (Owner.EngineId is int id) Owner.Bridge?.EnqueueRemoveComponent(id, TypeId, InstanceId);
        });
    }

    protected void Push(Action<GameComponent> mutate)
    {
        if (!Owner.SuppressPush && Owner.EngineId is int id)
            Owner.Bridge?.EnqueueMutateComponent(id, TypeId, mutate, InstanceId);
    }

    partial void OnEnabledChanged(bool value) => Push(c => c.Enabled = value);
    public virtual void Apply(GameComponent component, bool freezeFields)
    {
        InstanceId = component.InstanceId;
        if (!freezeFields) Enabled = component.Enabled;
    }
}

/// <summary>Register a view model here and a matching DataTemplate in the inspector for new types.</summary>
public static class ComponentEditorRegistry
{
    private static readonly Dictionary<string, Func<SceneObjectViewModel, ComponentViewModel>> Editors = new();
    static ComponentEditorRegistry()
    {
        Register(MaterialComponent.TypeId, owner => new MaterialInfo(owner));
        Register(PhysicsComponent.TypeId, owner => new PhysicsComponentViewModel(owner));
        Register(AudioComponent.TypeId, owner => new AudioComponentViewModel(owner));
        Register(ScriptBehaviourComponent.TypeId, owner => new ScriptBehaviourComponentViewModel(owner));
        Register(AnimatorComponent.TypeId, owner => new AnimatorComponentViewModel(owner));
    }

    public static void Register(string typeId, Func<SceneObjectViewModel, ComponentViewModel> factory) =>
        Editors.Add(typeId, factory);
    public static bool Supports(string typeId) => Editors.ContainsKey(typeId);
    public static ComponentViewModel? Create(string typeId, SceneObjectViewModel owner) =>
        Editors.TryGetValue(typeId, out var factory) ? factory(owner) : null;
}

public sealed class AddableComponentType
{
    public string DisplayName { get; }
    public IRelayCommand AddCommand { get; }

    public AddableComponentType(ComponentDefinition definition, SceneObjectViewModel owner)
    {
        DisplayName = definition.DisplayName;
        AddCommand = new RelayCommand(() =>
        {
            if (owner.EngineId is int id) owner.Bridge?.EnqueueAddComponent(id, definition.Id);
        }, () => owner.CanAddComponents && (definition.AllowMultiple ||
            !System.Linq.Enumerable.Any(owner.Components, c => c.TypeId == definition.Id)));
    }
}
