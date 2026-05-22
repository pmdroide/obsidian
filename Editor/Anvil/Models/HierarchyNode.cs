using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Anvil.Models;

public enum HierarchyIcon
{
    Folder,
    Camera,
    Light,
    Mesh,
}

public partial class HierarchyNode : ObservableObject
{
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private HierarchyIcon _icon = HierarchyIcon.Mesh;
    [ObservableProperty] private bool _isExpanded = true;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private int _depth;

    public ObservableCollection<HierarchyNode> Children { get; } = new();

    public bool HasChildren => Children.Count > 0;
}
