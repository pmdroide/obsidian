using System;
using CommunityToolkit.Mvvm.Input;
using Engine.Editor;

namespace Anvil.Models;

/// <summary>
/// One entry in the "+" add-object menu. The catalog is data-driven, so adding a new object type
/// is a single <c>AddableObjects.Add(...)</c> line in <c>MainWindowViewModel</c> — no XAML change.
/// Each entry owns its own command, so the MenuFlyout item binds straight to <see cref="AddCommand"/>
/// without any DataContext reach-back boilerplate.
/// </summary>
public sealed class AddableObjectType
{
    private readonly Action<IEditorBridge> _spawn;
    private readonly IEditorBridge? _bridge;

    public string DisplayName { get; }

    /// <summary>Runs <c>_spawn</c> against the live bridge (no-op until a bridge is attached).</summary>
    public IRelayCommand AddCommand { get; }

    public AddableObjectType(string displayName, Action<IEditorBridge> spawn, IEditorBridge? bridge)
    {
        DisplayName = displayName;
        _spawn = spawn;
        _bridge = bridge;
        AddCommand = new RelayCommand(Execute);
    }

    private void Execute()
    {
        if (_bridge != null) _spawn(_bridge);
    }
}
