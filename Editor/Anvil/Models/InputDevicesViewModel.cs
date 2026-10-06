using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Engine.Editor;
using Engine.InputDevices;

namespace Anvil.Models;

public partial class InputDevicesViewModel : ObservableObject
{
    private IEditorBridge? _bridge;
    private InputDeviceSnapshot _lastSnapshot = InputDeviceSnapshot.Waiting;
    [ObservableProperty] private string _keyboardStatus = "Waiting...";
    [ObservableProperty] private string _mouseStatus = "Waiting...";
    [ObservableProperty] private string _touchStatus = "Waiting...";
    [ObservableProperty] private string _penStatus = "Waiting...";
    [ObservableProperty] private string _controllerStatus = "Waiting...";
    [ObservableProperty] private string _statusMessage = "Waiting for input devices...";
    [ObservableProperty] private string _emptyControllerMessage = "Waiting for controller detection...";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasControllers))]
    private int _controllerCount;

    public bool HasControllers => ControllerCount > 0;
    public ObservableCollection<ControllerDeviceSnapshot> Controllers { get; } = new();

    public void AttachBridge(IEditorBridge bridge)
    {
        _bridge = bridge;
        Refresh();
    }

    public void Refresh()
    {
        if (_bridge == null) return;
        var snapshot = _bridge.InputDevices;
        if (ReferenceEquals(snapshot, _lastSnapshot)) return;
        _lastSnapshot = snapshot;
        KeyboardStatus = Availability(snapshot.KeyboardCount.HasValue ? snapshot.KeyboardCount > 0 : null);
        MouseStatus = Availability(snapshot.MouseCount.HasValue ? snapshot.MouseCount > 0 : null);
        TouchStatus = Availability(snapshot.TouchAvailable);
        PenStatus = Availability(snapshot.PenAvailable);
        ControllerStatus = snapshot.ControllerScanSucceeded
            ? snapshot.Controllers.Count == 0 ? "Not connected" : $"{snapshot.Controllers.Count} connected"
            : "Detection unavailable";
        StatusMessage = snapshot.Message;
        EmptyControllerMessage = snapshot.ControllerScanSucceeded
            ? "No controllers connected. Plug in or pair a controller to see it here."
            : "Controller detection is unavailable.";

        // Keep unchanged rows stable when checking for connected/disconnected controllers.
        for (int i = Controllers.Count - 1; i >= 0; i--)
            if (!snapshot.Controllers.Any(c => c.Slot == Controllers[i].Slot)) Controllers.RemoveAt(i);
        for (int i = 0; i < snapshot.Controllers.Count; i++)
        {
            var next = snapshot.Controllers[i];
            int existing = -1;
            for (int j = 0; j < Controllers.Count; j++)
                if (Controllers[j].Slot == next.Slot) { existing = j; break; }
            if (existing < 0) Controllers.Insert(i, next);
            else
            {
                if (Controllers[existing] != next) Controllers[existing] = next;
                if (existing != i) Controllers.Move(existing, i);
            }
        }
        ControllerCount = Controllers.Count;
    }

    private static string Availability(bool? available) => available switch
    {
        true => "Available",
        false => "Not detected",
        null => "Detection unavailable",
    };
}
