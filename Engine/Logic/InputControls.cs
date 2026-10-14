using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Engine.Logic
{
    /// <summary>What a control reports: a button (0 or 1), an axis (one float) or a 2D value (sticks, mouse delta).</summary>
    public enum InputControlType { Button, Axis, Vector2 }

    /// <summary>
    /// One physical control an input action can be bound to, addressed by a path such as
    /// <c>Keyboard/Space</c>, <c>Mouse/LeftButton</c>, <c>Mouse/Delta</c>, <c>GamePad/A</c> or
    /// <c>GamePad/LeftStick</c>. The paths are saved in <c>Content/System/InputActions.json</c>.
    /// </summary>
    public sealed class InputControl
    {
        public string Path { get; }
        public InputDevice Device { get; }
        /// <summary>The part of the path after the device ("Space", "LeftStick").</summary>
        public string Name { get; }
        /// <summary>For prompts and the editor: "Space", "Left Mouse", "LS".</summary>
        public string DisplayName { get; }
        public InputControlType Type { get; }

        internal readonly Keys Key;
        internal readonly int Id;

        internal InputControl(InputDevice device, string name, string displayName, InputControlType type, Keys key = Keys.None, int id = 0)
        {
            Device = device;
            Name = name;
            Path = InputControls.DeviceName(device) + "/" + name;
            DisplayName = displayName;
            Type = type;
            Key = key;
            Id = id;
        }

        public override string ToString() => Path;
    }

    /// <summary>
    /// Every control of the keyboard, the mouse and the (XInput) gamepads, how to read it, and which one
    /// went down this frame (for "press a key" rebinding). Values follow the gamepad convention: sticks,
    /// D-pad and <c>Mouse/Delta</c> have +Y up; triggers are 0..1; <c>Mouse/Delta</c> is in pixels and
    /// <c>Mouse/Scroll</c> in wheel notches. Gamepad controls read every connected pad and keep the
    /// strongest value, so any controller drives single-player actions.
    /// </summary>
    public static class InputControls
    {
        /// <summary>Analog values at or past this count as held (triggers, stick directions, modifiers).</summary>
        public const float PressPoint = 0.5f;

        private enum MouseId { LeftButton, RightButton, MiddleButton, XButton1, XButton2, Delta, DeltaX, DeltaY, Scroll, ScrollUp, ScrollDown }

        private enum PadId
        {
            A, B, X, Y, LeftShoulder, RightShoulder, LeftTrigger, RightTrigger, LeftStickButton, RightStickButton,
            Start, Back, DPadUp, DPadDown, DPadLeft, DPadRight, DPad,
            LeftStick, LeftStickX, LeftStickY, LeftStickUp, LeftStickDown, LeftStickLeft, LeftStickRight,
            RightStick, RightStickX, RightStickY, RightStickUp, RightStickDown, RightStickLeft, RightStickRight,
        }

        // Editor order: letters, digits, the usual gameplay keys, then the rest. Any other Keys name still parses.
        private static readonly Keys[] KeyboardOrder = BuildKeyboardOrder();

        private static readonly List<InputControl> AllControls = new();
        private static readonly Dictionary<string, InputControl> ByPath = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<InputDevice, List<InputControl>> ByDevice = new();

        static InputControls()
        {
            foreach (InputDevice d in Enum.GetValues(typeof(InputDevice))) ByDevice[d] = new List<InputControl>();

            foreach (Keys key in KeyboardOrder) AddKey(key, listed: true);

            AddMouse(MouseId.LeftButton, "Left Mouse", InputControlType.Button);
            AddMouse(MouseId.RightButton, "Right Mouse", InputControlType.Button);
            AddMouse(MouseId.MiddleButton, "Middle Mouse", InputControlType.Button);
            AddMouse(MouseId.XButton1, "Mouse Back", InputControlType.Button);
            AddMouse(MouseId.XButton2, "Mouse Forward", InputControlType.Button);
            AddMouse(MouseId.Delta, "Mouse Delta", InputControlType.Vector2);
            AddMouse(MouseId.DeltaX, "Mouse Delta X", InputControlType.Axis);
            AddMouse(MouseId.DeltaY, "Mouse Delta Y", InputControlType.Axis);
            AddMouse(MouseId.Scroll, "Scroll Wheel", InputControlType.Axis);
            AddMouse(MouseId.ScrollUp, "Scroll Up", InputControlType.Button);
            AddMouse(MouseId.ScrollDown, "Scroll Down", InputControlType.Button);

            AddPad(PadId.A, "A", InputControlType.Button);
            AddPad(PadId.B, "B", InputControlType.Button);
            AddPad(PadId.X, "X", InputControlType.Button);
            AddPad(PadId.Y, "Y", InputControlType.Button);
            AddPad(PadId.LeftShoulder, "LB", InputControlType.Button);
            AddPad(PadId.RightShoulder, "RB", InputControlType.Button);
            AddPad(PadId.LeftTrigger, "LT", InputControlType.Axis);
            AddPad(PadId.RightTrigger, "RT", InputControlType.Axis);
            AddPad(PadId.LeftStickButton, "LS Press", InputControlType.Button);
            AddPad(PadId.RightStickButton, "RS Press", InputControlType.Button);
            AddPad(PadId.Start, "Start", InputControlType.Button);
            AddPad(PadId.Back, "Back", InputControlType.Button);
            AddPad(PadId.DPadUp, "D-Pad Up", InputControlType.Button);
            AddPad(PadId.DPadDown, "D-Pad Down", InputControlType.Button);
            AddPad(PadId.DPadLeft, "D-Pad Left", InputControlType.Button);
            AddPad(PadId.DPadRight, "D-Pad Right", InputControlType.Button);
            AddPad(PadId.DPad, "D-Pad", InputControlType.Vector2);
            AddPad(PadId.LeftStick, "Left Stick", InputControlType.Vector2);
            AddPad(PadId.LeftStickX, "Left Stick X", InputControlType.Axis);
            AddPad(PadId.LeftStickY, "Left Stick Y", InputControlType.Axis);
            AddPad(PadId.LeftStickUp, "Left Stick Up", InputControlType.Axis);
            AddPad(PadId.LeftStickDown, "Left Stick Down", InputControlType.Axis);
            AddPad(PadId.LeftStickLeft, "Left Stick Left", InputControlType.Axis);
            AddPad(PadId.LeftStickRight, "Left Stick Right", InputControlType.Axis);
            AddPad(PadId.RightStick, "Right Stick", InputControlType.Vector2);
            AddPad(PadId.RightStickX, "Right Stick X", InputControlType.Axis);
            AddPad(PadId.RightStickY, "Right Stick Y", InputControlType.Axis);
            AddPad(PadId.RightStickUp, "Right Stick Up", InputControlType.Axis);
            AddPad(PadId.RightStickDown, "Right Stick Down", InputControlType.Axis);
            AddPad(PadId.RightStickLeft, "Right Stick Left", InputControlType.Axis);
            AddPad(PadId.RightStickRight, "Right Stick Right", InputControlType.Axis);
        }

        /// <summary>Every listed control, keyboard first.</summary>
        public static IReadOnlyList<InputControl> All => AllControls;

        /// <summary>The listed controls of one device, in editor order.</summary>
        public static IReadOnlyList<InputControl> For(InputDevice device) => ByDevice[device];

        public static string DeviceName(InputDevice device) => device switch
        {
            InputDevice.Keyboard => "Keyboard",
            InputDevice.Mouse => "Mouse",
            _ => "GamePad",
        };

        /// <summary>
        /// The control for a path ("Keyboard/Space", "Mouse/Delta", "GamePad/LeftStick"), case-insensitive;
        /// "Gamepad/" works too. Null for an empty or unknown path.
        /// </summary>
        public static InputControl Find(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            string key = path.Trim().Replace('\\', '/');
            // Paths compare case-insensitively, so "Gamepad/A" finds "GamePad/A".
            lock (ByPath)
                if (ByPath.TryGetValue(key, out InputControl control)) return control;
            // Keys missing from the editor list (media keys, IME keys...) still bind.
            int slash = key.IndexOf('/');
            if (slash > 0 && key.Substring(0, slash).Equals("Keyboard", StringComparison.OrdinalIgnoreCase) &&
                Enum.TryParse(key.Substring(slash + 1), true, out Keys k) && k != Keys.None)
                return AddKey(k, listed: false);
            return null;
        }

        public static string KeyPath(Keys key) => "Keyboard/" + key;

        /// <summary>"Space", "Left Mouse", "LS Press"; the raw path when it is unknown.</summary>
        public static string DisplayName(string path) => Find(path)?.DisplayName ?? (path ?? "");

        /// <summary>The control's current value (see the class summary for units).</summary>
        public static Vector2 Read(InputControl control)
        {
            if (control == null) return Vector2.Zero;
            switch (control.Device)
            {
                case InputDevice.Keyboard:
                    return GameInput.IsDown(control.Key) ? Vector2.UnitX : Vector2.Zero;
                case InputDevice.Mouse:
                    return ReadMouse((MouseId)control.Id);
                default:
                    Vector2 best = Vector2.Zero;
                    for (int i = 0; i < GameInput.Pads.Length; i++)
                    {
                        if (!GameInput.Pads[i].IsConnected) continue;
                        Vector2 v = ReadPad((PadId)control.Id, GameInput.Pads[i]);
                        if (v.LengthSquared() > best.LengthSquared()) best = v;
                    }
                    return best;
            }
        }

        /// <summary>The strength of a control as one positive number (a 2D value's length).</summary>
        public static float Magnitude(InputControl control)
        {
            Vector2 v = Read(control);
            return control?.Type == InputControlType.Vector2 ? v.Length() : Math.Abs(v.X);
        }

        /// <summary>
        /// The control that went down this frame (a key, mouse button, scroll notch, gamepad button, trigger or
        /// stick direction), optionally on one device only; null when nothing did. For rebinding menus.
        /// </summary>
        public static string DetectPressed(InputDevice? device = null)
        {
            if (device is null or InputDevice.Keyboard)
                foreach (Keys key in GameInput.KeysPressedThisFrame) return KeyPath(key);
            if (device is null or InputDevice.Mouse)
            {
                MouseState m = Input.mouseState, l = Input.mouseLastState;
                if (Rose(m.LeftButton, l.LeftButton)) return "Mouse/LeftButton";
                if (Rose(m.RightButton, l.RightButton)) return "Mouse/RightButton";
                if (Rose(m.MiddleButton, l.MiddleButton)) return "Mouse/MiddleButton";
                if (Rose(m.XButton1, l.XButton1)) return "Mouse/XButton1";
                if (Rose(m.XButton2, l.XButton2)) return "Mouse/XButton2";
                if (GameInput.MouseScroll > 0) return "Mouse/ScrollUp";
                if (GameInput.MouseScroll < 0) return "Mouse/ScrollDown";
            }
            if (device is null or InputDevice.GamePad)
                for (int i = 0; i < GameInput.Pads.Length; i++)
                {
                    string path = DetectGamePad(GameInput.Pads[i], GameInput.LastPads[i]);
                    if (path != null) return path;
                }
            return null;
        }

        /// <summary>
        /// The gamepad button, trigger or stick direction that crossed <see cref="PressPoint"/> between two
        /// states of one pad, or null. Anvil's Input Actions window uses it to listen for a controller.
        /// </summary>
        public static string DetectGamePad(GamePadState now, GamePadState last)
        {
            if (!now.IsConnected) return null;
            foreach (InputControl c in ByDevice[InputDevice.GamePad])
            {
                var id = (PadId)c.Id;
                // Whole sticks and signed axes are picked from the dropdown; their directions are detected.
                if (c.Type == InputControlType.Vector2 || id is PadId.LeftStickX or PadId.LeftStickY or PadId.RightStickX or PadId.RightStickY)
                    continue;
                if (ReadPad(id, now).X >= PressPoint && (!last.IsConnected || ReadPad(id, last).X < PressPoint)) return c.Path;
            }
            return null;
        }

        private static bool Rose(ButtonState now, ButtonState last) => now == ButtonState.Pressed && last == ButtonState.Released;

        private static Vector2 ReadMouse(MouseId id)
        {
            MouseState m = Input.mouseState;
            // Screen Y grows downwards; flip it so moving the mouse up is +Y like a stick.
            Vector2 delta = GameInput.MouseDelta * new Vector2(1, -1);
            int scroll = GameInput.MouseScroll;
            return id switch
            {
                MouseId.LeftButton => Button(m.LeftButton == ButtonState.Pressed),
                MouseId.RightButton => Button(m.RightButton == ButtonState.Pressed),
                MouseId.MiddleButton => Button(m.MiddleButton == ButtonState.Pressed),
                MouseId.XButton1 => Button(m.XButton1 == ButtonState.Pressed),
                MouseId.XButton2 => Button(m.XButton2 == ButtonState.Pressed),
                MouseId.Delta => delta,
                MouseId.DeltaX => new Vector2(delta.X, 0),
                MouseId.DeltaY => new Vector2(delta.Y, 0),
                MouseId.Scroll => new Vector2(scroll, 0),
                MouseId.ScrollUp => Button(scroll > 0),
                MouseId.ScrollDown => Button(scroll < 0),
                _ => Vector2.Zero,
            };
        }

        private static Vector2 ReadPad(PadId id, GamePadState s)
        {
            Vector2 l = s.ThumbSticks.Left, r = s.ThumbSticks.Right;
            return id switch
            {
                PadId.A => Button(s.IsButtonDown(Buttons.A)),
                PadId.B => Button(s.IsButtonDown(Buttons.B)),
                PadId.X => Button(s.IsButtonDown(Buttons.X)),
                PadId.Y => Button(s.IsButtonDown(Buttons.Y)),
                PadId.LeftShoulder => Button(s.IsButtonDown(Buttons.LeftShoulder)),
                PadId.RightShoulder => Button(s.IsButtonDown(Buttons.RightShoulder)),
                PadId.LeftTrigger => new Vector2(s.Triggers.Left, 0),
                PadId.RightTrigger => new Vector2(s.Triggers.Right, 0),
                PadId.LeftStickButton => Button(s.IsButtonDown(Buttons.LeftStick)),
                PadId.RightStickButton => Button(s.IsButtonDown(Buttons.RightStick)),
                PadId.Start => Button(s.IsButtonDown(Buttons.Start)),
                PadId.Back => Button(s.IsButtonDown(Buttons.Back)),
                PadId.DPadUp => Button(s.DPad.Up == ButtonState.Pressed),
                PadId.DPadDown => Button(s.DPad.Down == ButtonState.Pressed),
                PadId.DPadLeft => Button(s.DPad.Left == ButtonState.Pressed),
                PadId.DPadRight => Button(s.DPad.Right == ButtonState.Pressed),
                PadId.DPad => new Vector2(
                    (s.DPad.Right == ButtonState.Pressed ? 1 : 0) - (s.DPad.Left == ButtonState.Pressed ? 1 : 0),
                    (s.DPad.Up == ButtonState.Pressed ? 1 : 0) - (s.DPad.Down == ButtonState.Pressed ? 1 : 0)),
                PadId.LeftStick => l,
                PadId.LeftStickX => new Vector2(l.X, 0),
                PadId.LeftStickY => new Vector2(l.Y, 0),
                PadId.LeftStickUp => new Vector2(Math.Max(0, l.Y), 0),
                PadId.LeftStickDown => new Vector2(Math.Max(0, -l.Y), 0),
                PadId.LeftStickLeft => new Vector2(Math.Max(0, -l.X), 0),
                PadId.LeftStickRight => new Vector2(Math.Max(0, l.X), 0),
                PadId.RightStick => r,
                PadId.RightStickX => new Vector2(r.X, 0),
                PadId.RightStickY => new Vector2(r.Y, 0),
                PadId.RightStickUp => new Vector2(Math.Max(0, r.Y), 0),
                PadId.RightStickDown => new Vector2(Math.Max(0, -r.Y), 0),
                PadId.RightStickLeft => new Vector2(Math.Max(0, -r.X), 0),
                PadId.RightStickRight => new Vector2(Math.Max(0, r.X), 0),
                _ => Vector2.Zero,
            };
        }

        private static Vector2 Button(bool down) => down ? Vector2.UnitX : Vector2.Zero;

        private static InputControl AddKey(Keys key, bool listed)
        {
            var control = new InputControl(InputDevice.Keyboard, key.ToString(), KeyDisplayName(key), InputControlType.Button, key);
            Register(control, listed);
            return control;
        }

        private static void AddMouse(MouseId id, string display, InputControlType type) =>
            Register(new InputControl(InputDevice.Mouse, id.ToString(), display, type, id: (int)id), true);

        private static void AddPad(PadId id, string display, InputControlType type) =>
            Register(new InputControl(InputDevice.GamePad, id.ToString(), display, type, id: (int)id), true);

        private static void Register(InputControl control, bool listed)
        {
            lock (ByPath)
            {
                if (ByPath.ContainsKey(control.Path)) return;
                ByPath[control.Path] = control;
                if (!listed) return;
                AllControls.Add(control);
                ByDevice[control.Device].Add(control);
            }
        }

        private static Keys[] BuildKeyboardOrder()
        {
            var keys = new List<Keys>();
            for (Keys k = Keys.A; k <= Keys.Z; k++) keys.Add(k);
            for (Keys k = Keys.D0; k <= Keys.D9; k++) keys.Add(k);
            keys.AddRange(new[]
            {
                Keys.Space, Keys.Enter, Keys.Escape, Keys.Tab, Keys.Back,
                Keys.LeftShift, Keys.RightShift, Keys.LeftControl, Keys.RightControl, Keys.LeftAlt, Keys.RightAlt,
                Keys.Up, Keys.Down, Keys.Left, Keys.Right,
                Keys.Insert, Keys.Delete, Keys.Home, Keys.End, Keys.PageUp, Keys.PageDown, Keys.CapsLock,
                Keys.OemTilde, Keys.OemMinus, Keys.OemPlus, Keys.OemOpenBrackets, Keys.OemCloseBrackets, Keys.OemPipe,
                Keys.OemSemicolon, Keys.OemQuotes, Keys.OemComma, Keys.OemPeriod, Keys.OemQuestion, Keys.OemBackslash,
            });
            for (Keys k = Keys.F1; k <= Keys.F12; k++) keys.Add(k);
            for (Keys k = Keys.NumPad0; k <= Keys.NumPad9; k++) keys.Add(k);
            keys.AddRange(new[]
            {
                Keys.Add, Keys.Subtract, Keys.Multiply, Keys.Divide, Keys.Decimal, Keys.NumLock,
                Keys.PrintScreen, Keys.Scroll, Keys.Pause, Keys.LeftWindows, Keys.RightWindows, Keys.Apps,
            });
            return keys.ToArray();
        }

        private static string KeyDisplayName(Keys key)
        {
            if (key >= Keys.D0 && key <= Keys.D9) return ((int)(key - Keys.D0)).ToString();
            if (key >= Keys.NumPad0 && key <= Keys.NumPad9) return "Num " + (int)(key - Keys.NumPad0);
            switch (key)
            {
                case Keys.Back: return "Backspace";
                case Keys.Escape: return "Esc";
                case Keys.LeftControl: return "Left Ctrl";
                case Keys.RightControl: return "Right Ctrl";
                case Keys.Up: return "Up Arrow";
                case Keys.Down: return "Down Arrow";
                case Keys.Left: return "Left Arrow";
                case Keys.Right: return "Right Arrow";
                case Keys.OemTilde: return "`";
                case Keys.OemMinus: return "-";
                case Keys.OemPlus: return "=";
                case Keys.OemOpenBrackets: return "[";
                case Keys.OemCloseBrackets: return "]";
                case Keys.OemPipe: return "\\";
                case Keys.OemSemicolon: return ";";
                case Keys.OemQuotes: return "'";
                case Keys.OemComma: return ",";
                case Keys.OemPeriod: return ".";
                case Keys.OemQuestion: return "/";
                case Keys.OemBackslash: return "\\ (ISO)";
                case Keys.Add: return "Num +";
                case Keys.Subtract: return "Num -";
                case Keys.Multiply: return "Num *";
                case Keys.Divide: return "Num /";
                case Keys.Decimal: return "Num .";
                case Keys.LeftWindows: return "Left Windows";
                case Keys.RightWindows: return "Right Windows";
                case Keys.Apps: return "Menu";
            }
            // "PageUp" -> "Page Up", "F5" stays.
            string name = key.ToString();
            var sb = new StringBuilder(name.Length + 4);
            for (int i = 0; i < name.Length; i++)
            {
                if (i > 0 && char.IsUpper(name[i]) && char.IsLower(name[i - 1])) sb.Append(' ');
                sb.Append(name[i]);
            }
            return sb.ToString();
        }
    }
}
