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
}

public partial class AssetNode : ObservableObject
{
    [ObservableProperty] private string _id = string.Empty;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private AssetKind _kind = AssetKind.Folder;
    [ObservableProperty] private bool _isExpanded = true;
    [ObservableProperty] private bool _isSelected;

    public ObservableCollection<AssetNode> Children { get; } = new();

    public bool HasChildren => Children.Count > 0;
}
