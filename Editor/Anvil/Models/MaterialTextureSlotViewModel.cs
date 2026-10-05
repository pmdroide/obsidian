using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Engine.Components;

namespace Anvil.Models;

public partial class MaterialTextureSlotViewModel : ObservableObject
{
    private readonly Action<string?> _push;
    private readonly Func<MaterialComponent, string?> _read;
    public string Name { get; }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(Label))]
    private string? _path;

    public string Label => Path == null ? "From model" : Path.Length == 0 ? "None" : Path;

    public MaterialTextureSlotViewModel(string name, Func<MaterialComponent, string?> read, Action<string?> push)
    {
        Name = name;
        _read = read;
        _push = push;
    }

    partial void OnPathChanged(string? value) => _push(value);

    public void Assign(string path)
    {
        if (MaterialComponent.IsTextureAsset(path)) Path = path.Replace('\\', '/');
    }

    [RelayCommand] private void Clear() => Path = string.Empty;
    [RelayCommand] private void Reset() => Path = null;

    public void Apply(MaterialComponent component) => Path = _read(component);
}
