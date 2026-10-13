using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Engine.Editor;
using Engine.Recources;

namespace Engine.Logic
{
    /// <summary>
    /// What an action gives scripts. <b>Button</b>: pressed or not (<c>WasActionPressed("Jump")</c>);
    /// <b>Axis</b>: one float, usually -1..1 (<c>GetAxis("Zoom")</c>); <b>Vector2</b>: a 2D value such as
    /// movement or camera look (<c>GetVector("Move")</c>). Saved by name, so never rename one.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum InputActionType { Button, Axis, Vector2 }

    /// <summary>The slots of a composite binding: Vector2 actions use Up/Down/Left/Right, Axis actions Negative/Positive.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum InputCompositePart { Up, Down, Left, Right, Negative, Positive }

    /// <summary>
    /// One way to drive an action. Either a single control (<see cref="Path"/>, e.g. <c>Keyboard/Space</c>,
    /// <c>GamePad/LeftStick</c>, <c>Mouse/Delta</c>) or a composite of buttons that together make an axis or a
    /// 2D value (W/A/S/D in <see cref="Up"/>/<see cref="Left"/>/<see cref="Down"/>/<see cref="Right"/>, or
    /// Q/E in <see cref="Negative"/>/<see cref="Positive"/>). Paths: <see cref="InputControls"/>.
    /// </summary>
    public sealed class InputBinding
    {
        /// <summary>The control of a simple binding; null for a composite.</summary>
        public string Path { get; set; }

        public string Up { get; set; }
        public string Down { get; set; }
        public string Left { get; set; }
        public string Right { get; set; }
        public string Negative { get; set; }
        public string Positive { get; set; }

        /// <summary>Optional control that must be held for this binding to count ("Mouse/RightButton" + Mouse Delta = drag to look).</summary>
        public string Modifier { get; set; }

        /// <summary>Multiplies the value: mouse sensitivity, or degrees per second for a stick turning a camera.</summary>
        [JsonIgnore] public float Scale { get; set; } = 1f;

        /// <summary>Multiply by the frame time, so a held stick or key gives "per second" amounts that add up like mouse deltas.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool PerSecond { get; set; }

        /// <summary>Flip the X value (the value of a Button/Axis action).</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool InvertX { get; set; }

        /// <summary>Flip the Y value of a Vector2 action ("invert look").</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool InvertY { get; set; }

        // Written only when it isn't 1, to keep the file readable.
        [JsonInclude, JsonPropertyName("Scale")]
        private float? ScaleJson
        {
            get => Scale == 1f ? null : Scale;
            set => Scale = value ?? 1f;
        }

        public InputBinding() { }

        /// <summary>A simple binding to one control.</summary>
        public InputBinding(string path, float scale = 1f, bool perSecond = false, string modifier = null)
        {
            Path = path;
            Scale = scale;
            PerSecond = perSecond;
            Modifier = modifier;
        }

        /// <summary>A Vector2 composite (W, S, A, D order: up, down, left, right).</summary>
        public static InputBinding Composite2D(string up, string down, string left, string right) =>
            new InputBinding { Up = up, Down = down, Left = left, Right = right };

        /// <summary>An Axis composite: <paramref name="negative"/> gives -1, <paramref name="positive"/> +1.</summary>
        public static InputBinding Composite1D(string negative, string positive) =>
            new InputBinding { Negative = negative, Positive = positive };

        [JsonIgnore]
        public bool IsComposite => string.IsNullOrEmpty(Path) &&
            !(string.IsNullOrEmpty(Up) && string.IsNullOrEmpty(Down) && string.IsNullOrEmpty(Left) &&
              string.IsNullOrEmpty(Right) && string.IsNullOrEmpty(Negative) && string.IsNullOrEmpty(Positive));

        [JsonIgnore]
        public bool IsEmpty => string.IsNullOrEmpty(Path) && !IsComposite;

        public string GetPart(InputCompositePart part) => part switch
        {
            InputCompositePart.Up => Up,
            InputCompositePart.Down => Down,
            InputCompositePart.Left => Left,
            InputCompositePart.Right => Right,
            InputCompositePart.Negative => Negative,
            _ => Positive,
        };

        public void SetPart(InputCompositePart part, string path)
        {
            switch (part)
            {
                case InputCompositePart.Up: Up = path; break;
                case InputCompositePart.Down: Down = path; break;
                case InputCompositePart.Left: Left = path; break;
                case InputCompositePart.Right: Right = path; break;
                case InputCompositePart.Negative: Negative = path; break;
                default: Positive = path; break;
            }
        }

        /// <summary>Every control path this binding reads (its control or parts, then the modifier).</summary>
        public IEnumerable<string> Paths()
        {
            foreach (string p in new[] { Path, Up, Down, Left, Right, Negative, Positive, Modifier })
                if (!string.IsNullOrEmpty(p)) yield return p;
        }

        /// <summary>
        /// The device that drives this binding: its control's, or a composite's first part's (the modifier
        /// doesn't count). Keyboard for an empty binding.
        /// </summary>
        [JsonIgnore]
        public InputDevice Device
        {
            get
            {
                foreach (string p in new[] { Path, Up, Down, Left, Right, Negative, Positive })
                    if (InputControls.Find(p) is { } c) return c.Device;
                return InputDevice.Keyboard;
            }
        }

        /// <summary>"Space", "W/A/S/D", "Arrow Keys", "Right Mouse + Mouse Delta".</summary>
        [JsonIgnore]
        public string DisplayText
        {
            get
            {
                string main;
                if (!string.IsNullOrEmpty(Path)) main = InputControls.DisplayName(Path);
                else if (IsArrows()) main = "Arrow Keys";
                else
                {
                    // Keyboard order reads naturally: W/A/S/D, then Q/E.
                    var parts = new[] { Up, Left, Down, Right, Negative, Positive }
                        .Where(p => !string.IsNullOrEmpty(p)).Select(InputControls.DisplayName);
                    main = string.Join("/", parts);
                }
                return string.IsNullOrEmpty(Modifier) ? main : InputControls.DisplayName(Modifier) + " + " + main;
            }
        }

        private bool IsArrows()
        {
            static bool Is(string path, string key) => string.Equals(path, "Keyboard/" + key, StringComparison.OrdinalIgnoreCase);
            return Is(Up, "Up") && Is(Down, "Down") && Is(Left, "Left") && Is(Right, "Right");
        }

        public InputBinding Clone() => (InputBinding)MemberwiseClone();

        /// <summary>Same controls and settings (paths compare case-insensitively).</summary>
        public bool SameAs(InputBinding other)
        {
            if (other == null) return false;
            static bool Eq(string a, string b) => string.Equals(a ?? "", b ?? "", StringComparison.OrdinalIgnoreCase);
            return Eq(Path, other.Path) && Eq(Up, other.Up) && Eq(Down, other.Down) && Eq(Left, other.Left) &&
                   Eq(Right, other.Right) && Eq(Negative, other.Negative) && Eq(Positive, other.Positive) &&
                   Eq(Modifier, other.Modifier) && Scale == other.Scale && PerSecond == other.PerSecond &&
                   InvertX == other.InvertX && InvertY == other.InvertY;
        }

        public override string ToString() => DisplayText;
    }

    /// <summary>One action of the project's list: its name (what scripts ask for), type and bindings.</summary>
    public sealed class InputActionDefinition
    {
        public string Name { get; set; } = "";
        public InputActionType Type { get; set; } = InputActionType.Button;
        public List<InputBinding> Bindings { get; set; } = new List<InputBinding>();

        public InputActionDefinition() { }

        public InputActionDefinition(string name, InputActionType type, params InputBinding[] bindings)
        {
            Name = name;
            Type = type;
            Bindings.AddRange(bindings);
        }

        public InputActionDefinition Clone() => new InputActionDefinition
        {
            Name = Name,
            Type = Type,
            Bindings = Bindings.Select(b => b.Clone()).ToList(),
        };
    }

    /// <summary>
    /// The project's input actions, edited in Anvil > Window > Input Actions and persisted as
    /// <c>Content/System/InputActions.json</c>. Without the file the game uses <see cref="Defaults"/>.
    /// <see cref="InputActions"/> is the runtime side scripts read.
    ///
    /// Plain file I/O only: safe to call from the editor (UI) thread as well as the game thread.
    /// </summary>
    public static class InputActionList
    {
        //Content-relative. System/ is engine-managed and hidden from Anvil's Assets panel.
        public const string FileName = "System/InputActions.json";

        public sealed class Data
        {
            public List<InputActionDefinition> Actions { get; set; } = new List<InputActionDefinition>();
        }

        public static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        /// <summary>Reads InputActions.json, or <see cref="Defaults"/> when the file doesn't exist (or can't be read).</summary>
        public static Data Read()
        {
            string path = GameInfo.ResolveContentFile(FileName);
            try
            {
                if (File.Exists(path)) return FromJson(File.ReadAllText(path));
            }
            catch (Exception e)
            {
                EditorBridge.Log($"InputActionList: failed to read '{path}': {e.Message}");
            }
            return Defaults();
        }

        /// <summary>
        /// Writes the list to the source <c>Engine/Content</c> folder (the build copies it next to the exe).
        /// The running engine picks it up with <see cref="InputActions.RequestReload"/>.
        /// </summary>
        public static void Save(Data data)
        {
            string path = Path.Combine(GameInfo.SourceContentRoot, FileName);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, ToJson(data));
        }

        public static string ToJson(Data data) => JsonSerializer.Serialize(Normalize(data), JsonOptions);

        public static Data FromJson(string json) => Normalize(JsonSerializer.Deserialize<Data>(json, JsonOptions));

        /// <summary>
        /// The starting set, named like Unity's default actions: Move, Look, Jump, Sprint, Crouch, Interact,
        /// Attack, Previous, Next and Pause, each on keyboard/mouse and gamepad.
        /// </summary>
        public static Data Defaults() => new Data
        {
            Actions =
            {
                new InputActionDefinition("Move", InputActionType.Vector2,
                    InputBinding.Composite2D("Keyboard/W", "Keyboard/S", "Keyboard/A", "Keyboard/D"),
                    new InputBinding("GamePad/LeftStick")),
                // Degrees: 0.15 per pixel while the right button is held, 120/s on the arrows, 150/s on the stick.
                new InputActionDefinition("Look", InputActionType.Vector2,
                    new InputBinding("Mouse/Delta", 0.15f, modifier: "Mouse/RightButton"),
                    new InputBinding { Up = "Keyboard/Up", Down = "Keyboard/Down", Left = "Keyboard/Left", Right = "Keyboard/Right", Scale = 120f, PerSecond = true },
                    new InputBinding("GamePad/RightStick", 150f, perSecond: true)),
                new InputActionDefinition("Jump", InputActionType.Button,
                    new InputBinding("Keyboard/Space"), new InputBinding("GamePad/A")),
                new InputActionDefinition("Sprint", InputActionType.Button,
                    new InputBinding("Keyboard/LeftShift"), new InputBinding("Keyboard/RightShift"), new InputBinding("GamePad/LeftStickButton")),
                new InputActionDefinition("Crouch", InputActionType.Button,
                    new InputBinding("Keyboard/C"), new InputBinding("Keyboard/LeftControl"), new InputBinding("GamePad/B")),
                new InputActionDefinition("Interact", InputActionType.Button,
                    new InputBinding("Keyboard/E"), new InputBinding("GamePad/X")),
                new InputActionDefinition("Attack", InputActionType.Button,
                    new InputBinding("Mouse/LeftButton"), new InputBinding("GamePad/RightTrigger")),
                new InputActionDefinition("Previous", InputActionType.Button,
                    new InputBinding("Keyboard/D1"), new InputBinding("GamePad/LeftShoulder")),
                new InputActionDefinition("Next", InputActionType.Button,
                    new InputBinding("Keyboard/D2"), new InputBinding("GamePad/RightShoulder")),
                new InputActionDefinition("Pause", InputActionType.Button,
                    new InputBinding("Keyboard/Escape"), new InputBinding("GamePad/Start")),
            },
        };

        /// <summary>Drops nameless actions, later duplicates of a name (case-insensitive) and empty bindings; trims names and paths.</summary>
        public static Data Normalize(Data data)
        {
            var result = new Data();
            if (data?.Actions == null) return result;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (InputActionDefinition raw in data.Actions)
            {
                string name = raw?.Name?.Trim();
                if (string.IsNullOrEmpty(name) || !seen.Add(name)) continue;
                var action = new InputActionDefinition { Name = name, Type = raw.Type };
                foreach (InputBinding b in raw.Bindings ?? new List<InputBinding>())
                {
                    if (b == null) continue;
                    InputBinding copy = b.Clone();
                    copy.Path = Clean(copy.Path);
                    foreach (InputCompositePart part in Enum.GetValues(typeof(InputCompositePart)))
                        copy.SetPart(part, Clean(copy.GetPart(part)));
                    copy.Modifier = Clean(copy.Modifier);
                    if (!copy.IsEmpty) action.Bindings.Add(copy);
                }
                result.Actions.Add(action);
            }
            return result;
        }

        private static string Clean(string path) =>
            string.IsNullOrWhiteSpace(path) ? null : path.Trim().Replace('\\', '/');
    }
}
