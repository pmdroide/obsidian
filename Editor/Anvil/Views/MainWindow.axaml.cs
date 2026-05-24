using Anvil.Controls;
using Anvil.Services;
using Anvil.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Anvil.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // The MonoGameHost spins up the engine on its own thread and raises
        // BridgeReady on the UI thread once the bridge is constructed. Forward
        // that bridge into the view model so it can start reconciling snapshots.
        var host = this.FindControl<MonoGameHost>("ViewportHost");
        if (host != null)
        {
            host.BridgeReady += bridge =>
            {
                if (DataContext is MainWindowViewModel vm)
                    vm.AttachBridge(bridge);
            };
        }

        // Inspector focus tracking. When the user is typing in a NumericUpDown,
        // dragging a Slider, or interacting with a ColorPicker inside the inspector,
        // the reconciler must NOT push fresh engine values into the bound VM —
        // doing so steals focus from the control mid-edit. We listen for focus
        // changes window-wide and flag the reconciler whenever the focused
        // element is a descendant of the inspector ScrollViewer.
        AddHandler(InputElement.GotFocusEvent, OnAnyGotFocus, RoutingStrategies.Bubble);
        AddHandler(InputElement.LostFocusEvent, OnAnyLostFocus, RoutingStrategies.Bubble);
    }

    private void OnAnyGotFocus(object? sender, RoutedEventArgs e)
    {
        var inspector = this.FindControl<ScrollViewer>("InspectorScroll");
        BridgeReconciler.IsInspectorFocused =
            inspector != null && e.Source is Visual v && IsDescendantOf(v, inspector);
    }

    private void OnAnyLostFocus(object? sender, RoutedEventArgs e)
    {
        // LostFocus doesn't tell us where focus went; if focus stays within the
        // inspector a GotFocus will follow and re-set the flag. If it leaves the
        // window entirely we want the flag cleared so the reconciler resumes.
        var inspector = this.FindControl<ScrollViewer>("InspectorScroll");
        if (inspector == null) { BridgeReconciler.IsInspectorFocused = false; return; }
        if (e.Source is Visual v && IsDescendantOf(v, inspector))
        {
            // The control losing focus was inside the inspector. We can't know yet
            // whether focus is moving to another inspector control. Defer; the
            // following GotFocus (if any) will re-evaluate.
        }
    }

    private static bool IsDescendantOf(Visual node, Visual ancestor)
    {
        Visual? current = node;
        while (current != null)
        {
            if (ReferenceEquals(current, ancestor)) return true;
            current = current.GetVisualParent();
        }
        return false;
    }
}
