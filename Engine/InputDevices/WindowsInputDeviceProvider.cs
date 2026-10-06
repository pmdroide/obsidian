using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework.Input;

namespace Engine.InputDevices
{
    internal sealed class WindowsInputDeviceProvider : IInputDeviceProvider
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct RawInputDevice
        {
            public IntPtr Handle;
            public uint Type;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetRawInputDeviceList([Out] RawInputDevice[] devices,
            ref uint count, uint size);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int index);

        public InputDeviceSnapshot Capture()
        {
            int? keyboards = null, mice = null;
            bool? touch = null, pen = null;
            var errors = new List<string>();
            var controllers = new List<ControllerDeviceSnapshot>();
            bool controllerScanSucceeded = true;

            try
            {
                int digitizer = GetSystemMetrics(94); // SM_DIGITIZER: installed devices and readiness.
                bool ready = (digitizer & 0x80) != 0;
                touch = ready && (digitizer & 0x03) != 0;
                pen = ready && (digitizer & 0x0C) != 0;
                (keyboards, mice) = CountDesktopDevices();
            }
            catch (Exception ex) when (ex is Win32Exception || ex is DllNotFoundException ||
                ex is EntryPointNotFoundException)
            {
                errors.Add("Keyboard/mouse detection unavailable: " + ex.Message);
            }

            // WindowsDX exposes XInput controllers through MonoGame's GamePad API.
            // Read state first so cached capabilities cannot leave an unplugged pad listed.
            for (int slot = 0; slot < GamePad.MaximumGamePadCount; slot++)
            {
                try
                {
                    if (!GamePad.GetState(slot).IsConnected) continue;
                    var capabilities = GamePad.GetCapabilities(slot);
                    if (!capabilities.IsConnected) continue;
                    string type = capabilities.GamePadType.ToString();
                    string name = string.IsNullOrWhiteSpace(capabilities.DisplayName)
                        ? $"{type} controller" : capabilities.DisplayName;
                    var features = new List<string>();
                    if (capabilities.HasLeftXThumbStick || capabilities.HasLeftYThumbStick) features.Add("Left stick");
                    if (capabilities.HasRightXThumbStick || capabilities.HasRightYThumbStick) features.Add("Right stick");
                    if (capabilities.HasLeftTrigger || capabilities.HasRightTrigger) features.Add("Triggers");
                    if (capabilities.HasDPadUpButton || capabilities.HasDPadDownButton ||
                        capabilities.HasDPadLeftButton || capabilities.HasDPadRightButton) features.Add("D-pad");
                    if (capabilities.HasLeftVibrationMotor || capabilities.HasRightVibrationMotor) features.Add("Vibration");
                    controllers.Add(new ControllerDeviceSnapshot(slot, name, type,
                        features.Count == 0 ? "Buttons" : string.Join(" / ", features)));
                }
                catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException ||
                    ex is InvalidOperationException || ex is PlatformNotSupportedException)
                {
                    controllerScanSucceeded = false;
                    errors.Add("Controller detection unavailable: " + ex.Message);
                    break;
                }
            }
            return new InputDeviceSnapshot(keyboards, mice, touch, pen, controllers,
                controllerScanSucceeded, string.Join(Environment.NewLine, errors));
        }

        private static (int Keyboards, int Mice) CountDesktopDevices()
        {
            uint size = (uint)Marshal.SizeOf<RawInputDevice>();
            // Retry if a device arrives between the size query and the list query.
            for (int attempt = 0; attempt < 3; attempt++)
            {
                uint count = 0;
                if (GetRawInputDeviceList(null, ref count, size) == uint.MaxValue)
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                if (count == 0) return (0, 0);
                var devices = new RawInputDevice[count];
                uint result = GetRawInputDeviceList(devices, ref count, size);
                if (result == uint.MaxValue)
                {
                    int error = Marshal.GetLastWin32Error();
                    if (error == 122) continue; // ERROR_INSUFFICIENT_BUFFER during hot-plug.
                    throw new Win32Exception(error);
                }
                int keyboards = 0, mice = 0;
                for (int i = 0; i < result; i++)
                {
                    if (devices[i].Type == 0) mice++;
                    else if (devices[i].Type == 1) keyboards++;
                }
                return (keyboards, mice);
            }
            throw new Win32Exception(122);
        }
    }
}
