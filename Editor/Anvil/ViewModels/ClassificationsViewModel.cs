using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Engine.Editor;
using Engine.Recources;

namespace Anvil.ViewModels;

public sealed class ClassificationsViewModel
{
    public IReadOnlyList<ClassificationGroupViewModel> Groups { get; } = new[]
    {
        new ClassificationGroupViewModel(ClassificationKind.Layer, "Layers"),
        new ClassificationGroupViewModel(ClassificationKind.Tag, "Tags"),
        new ClassificationGroupViewModel(ClassificationKind.Role, "Roles"),
    };
    public void AttachBridge(IEditorBridge bridge) { foreach (var group in Groups) group.AttachBridge(bridge); }
    public void Refresh() { foreach (var group in Groups) group.Refresh(); }
}

public partial class ClassificationGroupViewModel : ObservableObject
{
    private IEditorBridge? _bridge;
    public ClassificationKind Kind { get; }
    public string Title { get; }
    public ObservableCollection<ClassificationEntryViewModel> Entries { get; } = new();
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(AddCommand))] private string _newName = "";
    [ObservableProperty] private string _error = "";
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(AddCommand))] private bool _isBusy;

    public ClassificationGroupViewModel(ClassificationKind kind, string title) { Kind = kind; Title = title; }
    public void AttachBridge(IEditorBridge bridge) { _bridge = bridge; Refresh(); AddCommand.NotifyCanExecuteChanged(); }

    public void Refresh()
    {
        if (_bridge == null) return;
        var definitions = _bridge.Classifications.Where(e => e.Kind == Kind).ToArray();
        for (int i = Entries.Count - 1; i >= 0; i--)
            if (!definitions.Any(e => e.Id == Entries[i].Id)) Entries.RemoveAt(i);
        foreach (var definition in definitions)
        {
            var row = Entries.FirstOrDefault(e => e.Id == definition.Id);
            if (row == null) Entries.Add(new ClassificationEntryViewModel(this, definition));
            else row.Apply(definition);
        }
    }

    private bool CanAdd() => _bridge != null && !IsBusy && !string.IsNullOrWhiteSpace(NewName);
    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void Add() => Edit(ClassificationEdit.Add, null, NewName);

    internal void Edit(ClassificationEdit edit, string? id, string? name)
    {
        if (_bridge == null || IsBusy) return;
        IsBusy = true;
        Error = "";
        _bridge.EnqueueEditClassification(edit, Kind, id, name, error => Dispatcher.UIThread.Post(() =>
        {
            IsBusy = false;
            Error = error ?? "";
            if (error == null && edit == ClassificationEdit.Add) NewName = "";
            Refresh();
        }));
    }
}

public partial class ClassificationEntryViewModel : ObservableObject
{
    private readonly ClassificationGroupViewModel _group;
    private string _savedName;
    public string Id { get; }
    public bool IsSystem { get; }
    public bool IsEditable => !IsSystem;
    public string KindLabel => IsSystem ? "System default" : "Custom";
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(RenameCommand))] private string _name;

    public ClassificationEntryViewModel(ClassificationGroupViewModel group, ClassificationDefinition definition)
    {
        _group = group; Id = definition.Id; IsSystem = definition.IsSystem; _name = _savedName = definition.Name;
    }
    internal void Apply(ClassificationDefinition definition)
    {
        // Keep an in-progress name while snapshots arrive; reconcile when the saved name actually changes.
        if (_savedName == definition.Name) return;
        _savedName = definition.Name;
        Name = definition.Name;
    }
    private bool CanRename() => IsEditable && !string.IsNullOrWhiteSpace(Name) && Name.Trim() != _savedName;
    [RelayCommand(CanExecute = nameof(CanRename))]
    private void Rename() => _group.Edit(ClassificationEdit.Rename, Id, Name);
    private bool CanRemove() => IsEditable;
    [RelayCommand(CanExecute = nameof(CanRemove))]
    private void Remove() => _group.Edit(ClassificationEdit.Remove, Id, null);
}
