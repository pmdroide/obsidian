using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Engine.Logic
{
    public enum InputDevice { Keyboard, Mouse, GamePad }

    /// <summary>
    /// Gameplay and menu input for scripts: keyboard (standalone and Anvil-forwarded keys), mouse
    /// and up to four XInput gamepads, with per-frame pressed edges. <see cref="MainSceneLogic"/>
    /// updates it once per frame, after <see cref="Input"/> and before scripts run.
    ///
    /// The Menu* properties merge arrows/WASD, the D-pad and the left stick, and repeat while held
    /// (after <see cref="RepeatDelay"/>, every <see cref="RepeatInterval"/>).
    ///
    /// Gameplay should prefer the project's input actions (<see cref="InputActions"/>, Anvil > Window > Input
    /// Actions): <see cref="WasActionPressed"/>, <see cref="GetVector"/>, ... They are updated at the end of
    /// <see cref="Update"/>, so they see this frame's keys, mouse and pads.
    /// </summary>
    public static class GameInput
    {
        public const float RepeatDelay = 0.4f;
        public const float RepeatInterval = 0.085f;
        private const float StickPress = 0.55f, StickRelease = 0.35f;
        // XInputGetState is slow for empty slots, so those are re-checked only this often.
        private const float DisconnectedPollSeconds = 1f;

        private static readonly Keys[] AllKeys = (Keys[])Enum.GetValues(typeof(Keys));
        private static HashSet<Keys> _keys = new HashSet<Keys>(), _lastKeys = new HashSet<Keys>();
        internal static readonly GamePadState[] Pads = new GamePadState[GamePad.MaximumGamePadCount];
        internal static readonly GamePadState[] LastPads = new GamePadState[GamePad.MaximumGamePadCount];
        // Checks set these instead of the hardware (GamePad.GetState).
        private static readonly GamePadState?[] PadOverrides = new GamePadState?[GamePad.MaximumGamePadCount];
        private static float _padPollTimer;

        private static readonly bool[] DirectionHeld = new bool[4];
        private static readonly float[] DirectionTimer = new float[4];
        private static readonly bool[] DirectionFired = new bool[4];
        private static readonly bool[] StickHeld = new bool[4];

        /// <summary>The device the player used last (for showing keyboard or gamepad prompts).</summary>
        public static InputDevice LastDevice { get; private set; } = InputDevice.Keyboard;

        public static bool MenuUp => DirectionFired[0];
        public static bool MenuDown => DirectionFired[1];
        public static bool MenuLeft => DirectionFired[2];
        public static bool MenuRight => DirectionFired[3];

        /// <summary>Enter, Space, gamepad A or Start.</summary>
        public static bool MenuConfirm =>
            WasPressed(Keys.Enter) || WasPressed(Keys.Space) || WasPressed(Buttons.A) || WasPressed(Buttons.Start);

        /// <summary>Escape, Backspace, gamepad B or Back.</summary>
        public static bool MenuBack =>
            WasPressed(Keys.Escape) || WasPressed(Keys.Back) || WasPressed(Buttons.B) || WasPressed(Buttons.Back);

        ////////////////////////////////////////////////////////////////////////////////
        //  INPUT ACTIONS (the project's list, see InputActions)
        ////////////////////////////////////////////////////////////////////////////////

        /// <summary>The project's action called <paramref name="name"/> (never null).</summary>
        public static InputAction Action(string name) => InputActions.Get(name);
        /// <summary>The action is held this frame.</summary>
        public static bool IsActionDown(string name) => InputActions.Get(name).IsPressed;
        /// <summary>The action went down this frame.</summary>
        public static bool WasActionPressed(string name) => InputActions.Get(name).WasPressed;
        /// <summary>The action came up this frame.</summary>
        public static bool WasActionReleased(string name) => InputActions.Get(name).WasReleased;
        /// <summary>An Axis action's value (a Button's 0..1, a Vector2's length).</summary>
        public static float GetAxis(string name) => InputActions.Get(name).Value;
        /// <summary>A Vector2 action's value (+Y up/forward).</summary>
        public static Vector2 GetVector(string name) => InputActions.Get(name).Vector;

        ////////////////////////////////////////////////////////////////////////////////
        //  RAW KEYS, BUTTONS AND MOUSE
        ////////////////////////////////////////////////////////////////////////////////

        public static bool IsDown(Keys key) => _keys.Contains(key);
        public static bool WasPressed(Keys key) => _keys.Contains(key) && !_lastKeys.Contains(key);
        public static bool WasReleased(Keys key) => !_keys.Contains(key) && _lastKeys.Contains(key);

        /// <summary>Held on any connected gamepad.</summary>
        public static bool IsDown(Buttons button)
        {
            for (int i = 0; i < Pads.Length; i++)
                if (Pads[i].IsConnected && Pads[i].IsButtonDown(button)) return true;
            return false;
        }

        /// <summary>Pressed this frame on any connected gamepad.</summary>
        public static bool WasPressed(Buttons button)
        {
            for (int i = 0; i < Pads.Length; i++)
                if (Pads[i].IsConnected && Pads[i].IsButtonDown(button) && !LastPads[i].IsButtonDown(button)) return true;
            return false;
        }

        public static bool AnyKeyPressed { get; private set; }
        public static bool AnyMouseButtonPressed { get; private set; }
        public static bool AnyGamePadButtonPressed { get; private set; }
        /// <summary>Any key, mouse button or gamepad button went down this frame ("press any button").</summary>
        public static bool AnyInputPressed => AnyKeyPressed || AnyMouseButtonPressed || AnyGamePadButtonPressed;

        /// <summary>Left stick of the first connected gamepad (Y up).</summary>
        public static Vector2 LeftStick => FirstPad().ThumbSticks.Left;
        /// <summary>Right stick of the first connected gamepad (Y up).</summary>
        public static Vector2 RightStick => FirstPad().ThumbSticks.Right;
        /// <summary>Left trigger of the first connected gamepad, 0..1.</summary>
        public static float LeftTrigger => FirstPad().Triggers.Left;
        /// <summary>Right trigger of the first connected gamepad, 0..1.</summary>
        public static float RightTrigger => FirstPad().Triggers.Right;

        /// <summary>Keys that went down this frame.</summary>
        public static IEnumerable<Keys> KeysPressedThisFrame
        {
            get
            {
                foreach (Keys key in _keys)
                    if (!_lastKeys.Contains(key)) yield return key;
            }
        }

        private static GamePadState FirstPad()
        {
            for (int i = 0; i < Pads.Length; i++)
                if (Pads[i].IsConnected) return Pads[i];
            return GamePadState.Default;
        }

        /// <summary>Cursor in viewport pixels.</summary>
        public static Point MousePosition => Input.mouseState.Position;
        public static bool MouseMoved => Input.mouseState.Position != Input.mouseLastState.Position;
        /// <summary>Left button went down this frame.</summary>
        public static bool MouseClicked =>
            Input.mouseState.LeftButton == ButtonState.Pressed && Input.mouseLastState.LeftButton == ButtonState.Released;
        /// <summary>Right button went down this frame.</summary>
        public static bool MouseRightClicked =>
            Input.mouseState.RightButton == ButtonState.Pressed && Input.mouseLastState.RightButton == ButtonState.Released;
        /// <summary>
        /// How far the cursor moved this frame, in pixels (screen axes: +Y down). Zero in Anvil while the cursor is
        /// over an editor panel, unless a drag that started in the viewport is still held.
        /// </summary>
        public static Vector2 MouseDelta { get; private set; }
        /// <summary>Scroll wheel notches this frame (positive = up).</summary>
        public static int MouseScroll => (Input.mouseState.ScrollWheelValue - Input.mouseLastState.ScrollWheelValue) / 120;

        public static void Update(float deltaSeconds)
        {
            (_lastKeys, _keys) = (_keys, _lastKeys);
            _keys.Clear();
            foreach (Keys key in AllKeys)
                if (key != Keys.None && Input.IsKeyDown(key)) _keys.Add(key);

            PollGamePads(deltaSeconds);

            AnyKeyPressed = false;
            foreach (Keys key in _keys)
                if (!_lastKeys.Contains(key)) { AnyKeyPressed = true; break; }

            MouseState m = Input.mouseState, lm = Input.mouseLastState;
            AnyMouseButtonPressed =
                (m.LeftButton == ButtonState.Pressed && lm.LeftButton == ButtonState.Released) ||
                (m.RightButton == ButtonState.Pressed && lm.RightButton == ButtonState.Released) ||
                (m.MiddleButton == ButtonState.Pressed && lm.MiddleButton == ButtonState.Released);

            AnyGamePadButtonPressed = false;
            for (int i = 0; i < Pads.Length && !AnyGamePadButtonPressed; i++)
                if (Pads[i].IsConnected && LastPads[i].IsConnected && Pads[i].Buttons != LastPads[i].Buttons)
                    AnyGamePadButtonPressed = PressedAnyButton(Pads[i], LastPads[i]);

            MouseDelta = MouseInViewport(m)
                ? new Vector2(m.X - lm.X, m.Y - lm.Y)
                : Vector2.Zero;

            if (AnyKeyPressed) LastDevice = InputDevice.Keyboard;
            else if (AnyGamePadButtonPressed || LeftStick.LengthSquared() > StickPress * StickPress ||
                     RightStick.LengthSquared() > StickPress * StickPress) LastDevice = InputDevice.GamePad;
            else if (AnyMouseButtonPressed || MouseDelta != Vector2.Zero) LastDevice = InputDevice.Mouse;

            UpdateDirections(deltaSeconds);
            InputActions.Update(deltaSeconds);
        }

        /// <summary>Uses <paramref name="state"/> for pad <paramref name="index"/> instead of the hardware; null to read it again (checks).</summary>
        internal static void OverrideGamePad(int index, GamePadState? state) => PadOverrides[index] = state;

        // Standalone: always. In Anvil the cursor must be over the viewport, or dragging from it
        // (Input.FilterHostMouse already released the buttons of a press that began over a panel).
        private static bool MouseInViewport(MouseState m)
        {
            if (Input.HostBridge == null || !Input.HostBridge.IsHostedByEditor) return true;
            if (m.LeftButton == ButtonState.Pressed || m.RightButton == ButtonState.Pressed || m.MiddleButton == ButtonState.Pressed)
                return true;
            return m.X >= 0 && m.Y >= 0 && m.X < Recources.GameSettings.g_screenwidth && m.Y < Recources.GameSettings.g_screenheight;
        }

        private static void PollGamePads(float dt)
        {
            _padPollTimer -= dt;
            bool pollEmpty = _padPollTimer <= 0f;
            if (pollEmpty) _padPollTimer = DisconnectedPollSeconds;
            for (int i = 0; i < Pads.Length; i++)
            {
                LastPads[i] = Pads[i];
                if (PadOverrides[i] is { } overridden) { Pads[i] = overridden; continue; }
                if (!Pads[i].IsConnected && !pollEmpty) continue;
                try { Pads[i] = GamePad.GetState(i); }
                catch { Pads[i] = GamePadState.Default; }
            }
        }

        private static bool PressedAnyButton(GamePadState now, GamePadState last)
        {
            foreach (Buttons b in FaceButtons)
                if (now.IsButtonDown(b) && !last.IsButtonDown(b)) return true;
            return false;
        }

        private static readonly Buttons[] FaceButtons =
        {
            Buttons.A, Buttons.B, Buttons.X, Buttons.Y, Buttons.Start, Buttons.Back,
            Buttons.LeftShoulder, Buttons.RightShoulder, Buttons.LeftTrigger, Buttons.RightTrigger,
            Buttons.DPadUp, Buttons.DPadDown, Buttons.DPadLeft, Buttons.DPadRight,
            Buttons.LeftStick, Buttons.RightStick,
        };

        private static void UpdateDirections(float dt)
        {
            Vector2 stick = LeftStick;
            UpdateStick(0, stick.Y);
            UpdateStick(1, -stick.Y);
            UpdateStick(2, -stick.X);
            UpdateStick(3, stick.X);

            bool[] held =
            {
                IsDown(Keys.Up) || IsDown(Keys.W) || IsDown(Buttons.DPadUp) || StickHeld[0],
                IsDown(Keys.Down) || IsDown(Keys.S) || IsDown(Buttons.DPadDown) || StickHeld[1],
                IsDown(Keys.Left) || IsDown(Keys.A) || IsDown(Buttons.DPadLeft) || StickHeld[2],
                IsDown(Keys.Right) || IsDown(Keys.D) || IsDown(Buttons.DPadRight) || StickHeld[3],
            };

            for (int d = 0; d < 4; d++)
            {
                DirectionFired[d] = false;
                if (!held[d])
                {
                    DirectionHeld[d] = false;
                    continue;
                }
                if (!DirectionHeld[d])
                {
                    DirectionHeld[d] = true;
                    DirectionFired[d] = true;
                    DirectionTimer[d] = RepeatDelay;
                    continue;
                }
                DirectionTimer[d] -= dt;
                if (DirectionTimer[d] <= 0f)
                {
                    DirectionFired[d] = true;
                    DirectionTimer[d] += RepeatInterval;
                }
            }
        }

        private static void UpdateStick(int direction, float value)
        {
            // Hysteresis so a stick resting near the threshold doesn't flicker.
            if (StickHeld[direction]) StickHeld[direction] = value > StickRelease;
            else StickHeld[direction] = value > StickPress;
        }
    }
}
