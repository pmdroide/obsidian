using System;
using System.Collections.Generic;
using System.Linq;

namespace Engine.InputDevices
{
    public sealed record ControllerDeviceSnapshot(int Slot, string Name, string Type, string Features)
    {
        public string PlayerLabel => $"Player {Slot + 1}";
    }

    public sealed class InputDeviceSnapshot
    {
        public int? KeyboardCount { get; }
        public int? MouseCount { get; }
        public bool? TouchAvailable { get; }
        public bool? PenAvailable { get; }
        public IReadOnlyList<ControllerDeviceSnapshot> Controllers { get; }
        public bool ControllerScanSucceeded { get; }
        public string Message { get; }

        public static InputDeviceSnapshot Waiting { get; } = new(null, null, null, null,
            Array.Empty<ControllerDeviceSnapshot>(), false, "Waiting for input devices...");

        public InputDeviceSnapshot(int? keyboardCount, int? mouseCount, bool? touchAvailable,
            bool? penAvailable, IEnumerable<ControllerDeviceSnapshot> controllers,
            bool controllerScanSucceeded = true, string message = "")
        {
            KeyboardCount = keyboardCount;
            MouseCount = mouseCount;
            TouchAvailable = touchAvailable;
            PenAvailable = penAvailable;
            Controllers = Array.AsReadOnly(controllers.ToArray());
            ControllerScanSucceeded = controllerScanSucceeded;
            Message = message;
        }
    }

    /// <summary>Polls device connections on the game thread and publishes immutable UI snapshots.</summary>
    public sealed class InputDeviceMonitor
    {
        private readonly IInputDeviceProvider _provider;
        private volatile InputDeviceSnapshot _snapshot = InputDeviceSnapshot.Waiting;
        private TimeSpan _untilNextScan;
        public InputDeviceSnapshot Snapshot => _snapshot;

        public InputDeviceMonitor() : this(new WindowsInputDeviceProvider()) { }
        internal InputDeviceMonitor(IInputDeviceProvider provider) => _provider = provider;

        public void Update(TimeSpan elapsed)
        {
            _untilNextScan -= elapsed;
            if (_untilNextScan > TimeSpan.Zero) return;
            _snapshot = _provider.Capture();
            _untilNextScan = TimeSpan.FromMilliseconds(500);
        }
    }

    internal interface IInputDeviceProvider
    {
        InputDeviceSnapshot Capture();
    }
}
