using Anvil.Controls;
using Anvil.Services;
using Anvil.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
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
        // LostFocus doesn't tell us where focus went, but the focused element on
        // the window does. If focus has truly left the inspector (window-level
        // focused element is no longer an inspector descendant), clear the flag
        // so the reconciler resumes writing values for the selected object.
        // Without this, leaving the inspector for the hierarchy or viewport
        // would leave the selected VM permanently frozen.
        var inspector = this.FindControl<ScrollViewer>("InspectorScroll");
        if (inspector == null) { BridgeReconciler.IsInspectorFocused = false; return; }

        // Defer to next dispatcher tick — the next GotFocus has not run yet when
        // LostFocus fires; reading FocusManager too early would still see the
        // outgoing element.
        Dispatcher.UIThread.Post(() =>
        {
            var focused = FocusManager?.GetFocusedElement() as Visual;
            BridgeReconciler.IsInspectorFocused =
                focused != null && IsDescendantOf(focused, inspector);
        });
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

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // 1. Only respond to standard left clicks
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            // 2. Walk up the visual tree from the clicked element (e.Source)
            // to see if the user clicked inside the Menu structure
            var visual = e.Source as Visual;
            while (visual != null && visual != sender)
            {
                if (visual is Menu || visual is MenuItem)
                {
                    // Click is on the menu; abort dragging and let the menu work normally
                    return; 
                }
                visual = visual.GetVisualParent();
            }

            // 3. If the click was on empty titlebar space, native dragging begins
            this.BeginMoveDrag(e);
        }
    }
}
