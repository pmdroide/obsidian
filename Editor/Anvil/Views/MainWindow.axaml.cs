using Anvil.Controls;
using Anvil.ViewModels;
using Avalonia.Controls;

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
    }
}
