using System;
using System.Collections.Generic;
using System.Linq;
using Engine.Editor;
using Microsoft.Xna.Framework;

namespace Engine.Logic
{
    /// <summary>
    /// A named action of the project's input list (Anvil > Window > Input Actions) with this frame's state.
    /// Get one with <c>GetAction("Jump")</c> in a script (or <see cref="InputActions.Get"/>); the instance stays
    /// the same for the whole session, so it can be kept in a field.
    ///
    /// Each frame every binding is read and the strongest one wins (keyboard and gamepad don't add up).
    /// </summary>
    public sealed class InputAction
    {
        /// <summary>Released again only below this, so an analog trigger resting near the press point doesn't flicker.</summary>
        public const float ReleasePoint = InputControls.PressPoint * 0.75f;

        private readonly List<InputBinding> _bindings = new();
        private List<InputBinding> _projectBindings = new();
        private Resolved[] _resolved = Array.Empty<Resolved>();
        private float _sincePressed = float.PositiveInfinity;

        internal InputAction(string name) { Name = name; }

        public string Name { get; }
        public InputActionType Type { get; private set; }
        /// <summary>False when the project list has no action of this name (it then never fires).</summary>
        public bool Exists { get; internal set; }
        /// <summary>A disabled action reads as released and zero. Restored on Anvil's Stop.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>Held this frame (for an axis or 2D value: pushed at least halfway).</summary>
        public bool IsPressed { get; private set; }
        /// <summary>Went down this frame.</summary>
        public bool WasPressed { get; private set; }
        /// <summary>Came up this frame.</summary>
        public bool WasReleased { get; private set; }
        /// <summary>Button: 0..1 (analog triggers in between). Axis: the signed value. Vector2: the length.</summary>
        public float Value { get; private set; }
        /// <summary>Vector2: the value (+Y up / forward). Button and Axis: (<see cref="Value"/>, 0).</summary>
        public Vector2 Vector { get; private set; }
        /// <summary>Seconds it has been held (0 when released).</summary>
        public float HeldTime { get; private set; }
        /// <summary>The device of the binding that drove it last, for showing the right prompt.</summary>
        public InputDevice ActiveDevice { get; private set; } = InputDevice.Keyboard;

        /// <summary>The current bindings (the project's, unless a script rebound them).</summary>
        public IReadOnlyList<InputBinding> Bindings => _bindings;

        /// <summary>True when the bindings differ from the project's (a player rebound it).</summary>
        public bool IsOverridden =>
            _bindings.Count != _projectBindings.Count || _bindings.Where((b, i) => !b.SameAs(_projectBindings[i])).Any();

        /// <summary>
        /// Pressed within the last <paramref name="seconds"/> and not consumed: input buffering, e.g. a jump
        /// pressed just before landing. Call <see cref="ConsumePress"/> once you act on it.
        /// </summary>
        public bool WasPressedWithin(float seconds) => _sincePressed <= seconds;

        /// <summary>Forgets the last press, so <see cref="WasPressedWithin"/> doesn't fire for it again.</summary>
        public void ConsumePress() => _sincePressed = float.PositiveInfinity;

        /// <summary>
        /// The binding to show in a prompt ("Space", "A", "W/A/S/D") for <paramref name="device"/>, by default
        /// the device the player used last (<see cref="GameInput.LastDevice"/>; mouse and keyboard count as one).
        /// Empty when the action has no binding on it.
        /// </summary>
        public string BindingText(InputDevice? device = null)
        {
            InputDevice want = device ?? GameInput.LastDevice;
            InputBinding exact = _bindings.FirstOrDefault(b => b.Device == want);
            if (exact != null) return exact.DisplayText;
            bool desktop = want != InputDevice.GamePad;
            return _bindings.FirstOrDefault(b => (b.Device != InputDevice.GamePad) == desktop)?.DisplayText ?? "";
        }

        /// <summary>Index of the first binding on <paramref name="device"/> (mouse and keyboard count as one), or -1.</summary>
        public int FindBinding(InputDevice device)
        {
            for (int i = 0; i < _bindings.Count; i++)
                if (_bindings[i].Device == device) return i;
            bool desktop = device != InputDevice.GamePad;
            for (int i = 0; i < _bindings.Count; i++)
                if ((_bindings[i].Device != InputDevice.GamePad) == desktop) return i;
            return -1;
        }

        /// <summary>Replaces the control of binding <paramref name="index"/> (a simple binding), e.g. from a rebinding menu.</summary>
        public void SetBinding(int index, string path)
        {
            if (index < 0 || index >= _bindings.Count) return;
            InputBinding b = _bindings[index].Clone();
            b.Path = path;
            foreach (InputCompositePart part in Enum.GetValues(typeof(InputCompositePart))) b.SetPart(part, null);
            _bindings[index] = b;
            Resolve();
        }

        /// <summary>Replaces one part of a composite binding (e.g. the Up key of W/A/S/D).</summary>
        public void SetBindingPart(int index, InputCompositePart part, string path)
        {
            if (index < 0 || index >= _bindings.Count) return;
            InputBinding b = _bindings[index].Clone();
            b.SetPart(part, path);
            _bindings[index] = b;
            Resolve();
        }

        /// <summary>
        /// Points the first simple binding of the control's device at <paramref name="path"/> (adds one when there is
        /// none). The usual "press a key for Jump" rebind: <c>action.Rebind(InputActions.ListenForControl())</c>.
        /// </summary>
        public bool Rebind(string path)
        {
            InputControl control = InputControls.Find(path);
            if (control == null) return false;
            for (int i = 0; i < _bindings.Count; i++)
                if (!_bindings[i].IsComposite && _bindings[i].Device == control.Device) { SetBinding(i, control.Path); return true; }
            AddBinding(new InputBinding(control.Path));
            return true;
        }

        public void AddBinding(InputBinding binding)
        {
            if (binding == null) return;
            _bindings.Add(binding.Clone());
            Resolve();
        }

        public void RemoveBinding(int index)
        {
            if (index < 0 || index >= _bindings.Count) return;
            _bindings.RemoveAt(index);
            Resolve();
        }

        public void SetBindings(IEnumerable<InputBinding> bindings)
        {
            _bindings.Clear();
            if (bindings != null) _bindings.AddRange(bindings.Where(b => b != null).Select(b => b.Clone()));
            Resolve();
        }

        /// <summary>Back to the project's bindings.</summary>
        public void ResetBindings() => SetBindings(_projectBindings);

        public override string ToString() => $"{Name} ({Type})";

        internal void Define(InputActionDefinition definition)
        {
            Exists = definition != null;
            Type = definition?.Type ?? InputActionType.Button;
            _projectBindings = definition?.Bindings.Select(b => b.Clone()).ToList() ?? new List<InputBinding>();
            SetBindings(_projectBindings);
        }

        internal void Update(float dt, bool blocked)
        {
            Vector2 best = Vector2.Zero;
            float bestMagnitude = 0f;
            InputDevice device = ActiveDevice;
            if (Enabled && !blocked)
                foreach (Resolved r in _resolved)
                {
                    if (r.Modifier != null && InputControls.Magnitude(r.Modifier) < InputControls.PressPoint) continue;
                    Vector2 v = r.Read(Type, dt);
                    // An axis compares its signed X; a button bound to a stick counts how far it is pushed.
                    float magnitude = Type == InputActionType.Axis ? Math.Abs(v.X) : v.Length();
                    if (magnitude <= bestMagnitude) continue;
                    best = v;
                    bestMagnitude = magnitude;
                    device = r.Device;
                }

            switch (Type)
            {
                case InputActionType.Vector2:
                    Vector = best;
                    Value = bestMagnitude;
                    break;
                case InputActionType.Axis:
                    Value = best.X;
                    Vector = new Vector2(Value, 0);
                    break;
                default:
                    Value = bestMagnitude;
                    Vector = new Vector2(Value, 0);
                    break;
            }
            if (bestMagnitude > 0f) ActiveDevice = device;

            bool was = IsPressed;
            IsPressed = was ? bestMagnitude >= ReleasePoint : bestMagnitude >= InputControls.PressPoint;
            WasPressed = IsPressed && !was;
            WasReleased = !IsPressed && was;
            HeldTime = IsPressed ? HeldTime + (WasPressed ? 0f : dt) : 0f;
            if (WasPressed) _sincePressed = 0f;
            else if (!float.IsPositiveInfinity(_sincePressed)) _sincePressed += dt;
        }

        private void Resolve() => _resolved = _bindings.Select(b => new Resolved(b)).ToArray();

        /// <summary>A binding with its paths looked up once.</summary>
        private sealed class Resolved
        {
            private readonly InputBinding _binding;
            private readonly InputControl _control, _up, _down, _left, _right, _negative, _positive;
            public readonly InputControl Modifier;
            public readonly InputDevice Device;

            public Resolved(InputBinding b)
            {
                _binding = b;
                _control = InputControls.Find(b.Path);
                _up = InputControls.Find(b.Up);
                _down = InputControls.Find(b.Down);
                _left = InputControls.Find(b.Left);
                _right = InputControls.Find(b.Right);
                _negative = InputControls.Find(b.Negative);
                _positive = InputControls.Find(b.Positive);
                Modifier = InputControls.Find(b.Modifier);
                Device = b.Device;
            }

            public Vector2 Read(InputActionType type, float dt)
            {
                Vector2 v;
                if (_control != null) v = InputControls.Read(_control);
                else if (!_binding.IsComposite) return Vector2.Zero;
                else if (type == InputActionType.Vector2)
                {
                    v = new Vector2(Max(_right, _positive) - Max(_left, _negative), Part(_up) - Part(_down));
                    // Diagonals (W + D) are as fast as straight lines.
                    if (v.LengthSquared() > 1f) v.Normalize();
                }
                else v = new Vector2(Math.Max(Part(_positive), Max(_right, _up)) - Math.Max(Part(_negative), Max(_left, _down)), 0);

                if (_binding.InvertX) v.X = -v.X;
                if (_binding.InvertY) v.Y = -v.Y;
                v *= _binding.Scale;
                if (_binding.PerSecond) v *= dt;
                return v;
            }

            private static float Part(InputControl c) => c == null ? 0f : MathHelper.Clamp(InputControls.Magnitude(c), 0f, 1f);
            private static float Max(InputControl a, InputControl b) => Math.Max(Part(a), Part(b));
        }
    }

    /// <summary>
    /// The project's input actions at runtime. Loaded from <see cref="InputActionList"/> on first use and updated
    /// at the end of <see cref="GameInput.Update"/>, before scripts run. While the debug console is open every
    /// action reads as released.
    ///
    /// Scripts normally use the <see cref="Scripting.ScriptBehaviour"/> shortcuts (<c>WasActionPressed("Jump")</c>,
    /// <c>GetVector("Move")</c>, ...). Player rebinding: <see cref="InputAction.Rebind"/> with
    /// <see cref="ListenForControl"/>, kept with <see cref="SaveBindings"/>/<see cref="LoadBindings"/>.
    /// Game thread only, except <see cref="RequestReload"/>.
    /// </summary>
    public static class InputActions
    {
        /// <summary>Default <see cref="SaveData"/> name of the player's rebinds.</summary>
        public const string DefaultBindingsSave = "InputBindings";

        private static readonly Dictionary<string, InputAction> ByName = new(StringComparer.OrdinalIgnoreCase);
        private static readonly List<InputAction> Listed = new();
        private static bool _loaded;
        private static volatile bool _reloadRequested;

        /// <summary>The project's actions in list order.</summary>
        public static IReadOnlyList<InputAction> All
        {
            get
            {
                EnsureLoaded();
                return Listed;
            }
        }

        /// <summary>
        /// The action called <paramref name="name"/> (case-insensitive). Never null: an unknown name logs once and
        /// returns an action that never fires (<see cref="InputAction.Exists"/> false), which comes alive if the
        /// project adds it later.
        /// </summary>
        public static InputAction Get(string name)
        {
            EnsureLoaded();
            name = name?.Trim() ?? "";
            if (ByName.TryGetValue(name, out InputAction action)) return action;
            action = new InputAction(name);
            action.Define(null);
            ByName[name] = action;
            EditorBridge.Log($"Input: no action named '{name}'. Add it in Anvil > Window > Input Actions.");
            return action;
        }

        public static bool Has(string name)
        {
            EnsureLoaded();
            return name != null && ByName.TryGetValue(name.Trim(), out InputAction a) && a.Exists;
        }

        /// <summary>Reads the project's list again: actions keep their instances, bindings and Enabled go back to the project's.</summary>
        public static void Load() => Apply(InputActionList.Read());

        /// <summary>Uses <paramref name="data"/> as the project's list (Anvil and the checks; the game calls <see cref="Load"/>).</summary>
        public static void Apply(InputActionList.Data data)
        {
            data = InputActionList.Normalize(data);
            _loaded = true;
            Listed.Clear();
            foreach (InputAction a in ByName.Values) a.Define(null);
            foreach (InputActionDefinition definition in data.Actions)
            {
                if (!ByName.TryGetValue(definition.Name, out InputAction action))
                    ByName[definition.Name] = action = new InputAction(definition.Name);
                action.Define(definition);
                action.Enabled = true;
                Listed.Add(action);
            }
        }

        /// <summary>Thread-safe: reload the list from disk before the next update (Anvil saved it).</summary>
        public static void RequestReload() => _reloadRequested = true;

        /// <summary>Called by <see cref="GameInput.Update"/> once per frame.</summary>
        internal static void Update(float dt)
        {
            if (_reloadRequested)
            {
                _reloadRequested = false;
                Load();
            }
            EnsureLoaded();
            bool blocked = DebugScreen.ConsoleOpen;
            foreach (InputAction action in Listed) action.Update(dt, blocked);
        }

        /// <summary>
        /// The control that went down this frame as a binding path ("Keyboard/K", "GamePad/RightTrigger"), or null.
        /// Call it every frame while a rebinding menu waits for the player, then <see cref="InputAction.Rebind"/>.
        /// </summary>
        public static string ListenForControl(InputDevice? device = null) => InputControls.DetectPressed(device);

        /// <summary>Every action back to the project's bindings.</summary>
        public static void ResetBindings()
        {
            foreach (InputAction action in All) action.ResetBindings();
        }

        /// <summary>Saves the bindings the player changed (overridden actions only) with <see cref="SaveData"/>.</summary>
        public static bool SaveBindings(string saveName = DefaultBindingsSave)
        {
            var data = new BindingOverrides();
            foreach (InputAction action in All.Where(a => a.IsOverridden))
                data.Actions[action.Name] = action.Bindings.Select(b => b.Clone()).ToList();
            return SaveData.Save(saveName, data);
        }

        /// <summary>
        /// Applies rebinds saved by <see cref="SaveBindings"/> (call it when the game starts, e.g. in the main menu's
        /// Start). Actions the save doesn't mention keep the project's bindings. False when there is no save.
        /// </summary>
        public static bool LoadBindings(string saveName = DefaultBindingsSave)
        {
            if (!SaveData.TryLoad(saveName, out BindingOverrides data) || data?.Actions == null) return false;
            ResetBindings();
            foreach (var entry in data.Actions)
                if (Has(entry.Key)) Get(entry.Key).SetBindings(entry.Value);
            return true;
        }

        private static void EnsureLoaded()
        {
            if (!_loaded) Load();
        }

        /// <summary>The save format of <see cref="SaveBindings"/>: action name to its bindings.</summary>
        public sealed class BindingOverrides
        {
            public Dictionary<string, List<InputBinding>> Actions { get; set; } = new();
        }
    }
}
