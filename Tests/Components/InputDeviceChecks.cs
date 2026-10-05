using Anvil.Models;
using Anvil.ViewModels;
using Engine.Editor;
using Engine.InputDevices;

internal static class InputDeviceChecks
{
    public static void Run()
    {
        var provider = new FakeProvider();
        var monitor = new InputDeviceMonitor(provider);
        var bridge = new EditorBridge();
        bridge.BindInputDevices(monitor);
        var panel = new InputDevicesViewModel();
        panel.AttachBridge(bridge);
        Check(panel.KeyboardStatus == "Waiting..." && !panel.HasControllers,
            "Input panel waits for the first device scan");

        monitor.Update(TimeSpan.Zero);
        panel.Refresh();
        Check(panel.KeyboardStatus == "Available" && panel.MouseStatus == "Available" &&
            panel.TouchStatus == "Not detected" && panel.PenStatus == "Not detected" &&
            panel.ControllerStatus == "Not connected" && !panel.HasControllers,
            "Input panel reflects desktop devices and no-controller state");
        monitor.Update(TimeSpan.FromMilliseconds(200));
        Check(provider.Scans == 1, "device discovery is throttled between frames");

        var connected = new List<ControllerDeviceSnapshot>
        {
            new(1, "Test gamepad", "GamePad", "Left stick / Triggers"),
            new(3, "Test wheel", "Wheel", "Vibration"),
        };
        provider.Next = new InputDeviceSnapshot(1, 1, true, true, connected);
        connected.Clear();
        monitor.Update(TimeSpan.FromMilliseconds(300));
        panel.Refresh();
        Check(panel.ControllerCount == 2 && panel.Controllers[0].PlayerLabel == "Player 2" &&
            panel.Controllers[1].PlayerLabel == "Player 4" && panel.TouchStatus == "Available" &&
            panel.PenStatus == "Available", "connected controllers list their actual player slots and extra input devices");
        var previous = bridge.InputDevices;
        var firstRow = panel.Controllers[0];
        provider.Next = new InputDeviceSnapshot(1, 1, true, true, previous.Controllers);
        monitor.Update(TimeSpan.FromMilliseconds(500));
        panel.Refresh();
        Check(ReferenceEquals(panel.Controllers[0], firstRow), "unchanged controller rows stay stable during refresh");

        provider.Next = new InputDeviceSnapshot(0, 1, false, false,
            new[] { new ControllerDeviceSnapshot(3, "Replacement wheel", "Wheel", "Triggers") });
        monitor.Update(TimeSpan.FromMilliseconds(500));
        panel.Refresh();
        Check(panel.ControllerCount == 1 && panel.Controllers[0].Name == "Replacement wheel" &&
            panel.Controllers[0].Slot == 3 && panel.KeyboardStatus == "Not detected",
            "unplugged controllers disappear and replacement devices update the list");
        Check(previous.Controllers.Count == 2 && previous.Controllers[0].Name == "Test gamepad",
            "published device snapshots remain independent of later scans and source collections");

        provider.Next = new InputDeviceSnapshot(null, 1, false, false,
            Array.Empty<ControllerDeviceSnapshot>(), false, "Controller detection unavailable.");
        monitor.Update(TimeSpan.FromMilliseconds(500));
        panel.Refresh();
        Check(!panel.HasControllers && panel.KeyboardStatus == "Detection unavailable" &&
            panel.MouseStatus == "Available" && panel.ControllerStatus == "Detection unavailable" &&
            panel.EmptyControllerMessage == "Controller detection is unavailable.",
            "detection failures are distinct from disconnected devices and preserve known statuses");
        provider.Next = new InputDeviceSnapshot(1, 1, false, false, Array.Empty<ControllerDeviceSnapshot>());
        monitor.Update(TimeSpan.FromMilliseconds(500));
        panel.Refresh();
        Check(panel.StatusMessage == "" && panel.ControllerStatus == "Not connected" && !panel.HasControllers,
            "a later successful scan clears errors and restores the empty-controller state");

        var main = new MainWindowViewModel();
        main.SetInspectorViewCommand.Execute("Input");
        Check(main.IsInspectorInputView && !main.IsInspectorSteamView && !main.IsInspectorSelectionView,
            "Window Input opens the Input inspector section");
        Console.WriteLine("Input device discovery and editor checks passed.");
    }

    public static void RunNative()
    {
        var monitor = new InputDeviceMonitor();
        monitor.Update(TimeSpan.Zero);
        var snapshot = monitor.Snapshot;
        Check(snapshot.KeyboardCount.HasValue && snapshot.MouseCount.HasValue && snapshot.ControllerScanSucceeded,
            "native Windows keyboard, mouse and controller discovery succeeds: " + snapshot.Message);
        Console.WriteLine($"Native input scan: keyboards={snapshot.KeyboardCount}, mice={snapshot.MouseCount}, " +
            $"controllers={snapshot.Controllers.Count}, touch={snapshot.TouchAvailable}, pen={snapshot.PenAvailable}.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("Input device check failed: " + message);
    }

    private sealed class FakeProvider : IInputDeviceProvider
    {
        public int Scans;
        public InputDeviceSnapshot Next = new(1, 1, false, false, Array.Empty<ControllerDeviceSnapshot>());
        public InputDeviceSnapshot Capture() { Scans++; return Next; }
    }
}
