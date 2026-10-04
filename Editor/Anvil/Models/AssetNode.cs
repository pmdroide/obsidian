using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Anvil.Models;

public enum AssetKind
{
    Folder,
    Scene,
    Mesh,
    Texture,
    Script,
    Audio,
    Material,
    Shader,
    Font,
    Video,
    File,
}

public partial class AssetNode : ObservableObject
{
    [ObservableProperty] private string _id = string.Empty;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private AssetKind _kind = AssetKind.Folder;
    [ObservableProperty] private bool _isExpanded = true;
    [ObservableProperty] private bool _isSelected;

    /// <summary>Path relative to the Engine/Content folder, '/'-separated.</summary>
    public string RelativePath { get; init; } = string.Empty;

    /// <summary>Registered model key this mesh file loads as (drag/double-click to spawn), or null.</summary>
    public string? ModelKey { get; init; }

    /// <summary>Imported model whose GameObjects/Models/{key} folder contains this node (texture-drop / delete target), or null.</summary>
    public string? OwnerModelKey { get; init; }

    public ObservableCollection<AssetNode> Children { get; } = new();

    public bool HasChildren => Children.Count > 0;
}
