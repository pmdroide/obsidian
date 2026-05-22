using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Anvil.Models;

public enum AssetKind
{
    Folder,
    Scene,
    Mesh,
    Texture,
}

public partial class AssetNode : ObservableObject
{
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private AssetKind _kind = AssetKind.Folder;
    [ObservableProperty] private bool _isExpanded = true;
    [ObservableProperty] private int _depth;

    public ObservableCollection<AssetNode> Children { get; } = new();
}
