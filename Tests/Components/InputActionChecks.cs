using Anvil.Services;
using Anvil.ViewModels;
using Avalonia.Input;
using Engine.Logic;
using Engine.Recources;
using Engine.Scripting;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using XnaKeys = Microsoft.Xna.Framework.Input.Keys;

/// <summary>Input actions: the project file, runtime evaluation, rebinding, listening and Anvil's editor.</summary>
internal static class InputActionChecks
{
    private const float Dt = 1f / 60f;
    private static MouseState _mouse;

    private sealed class Probe : ScriptBehaviour { }

    public static void Run()
    {
        KeyboardState keyboard = Input.keyboardState;
        MouseState mouse = Input.mouseState, lastMouse = Input.mouseLastState;
        string saveFolder = SaveData.Folder;
        string folder = Path.Combine(Path.GetTempPath(), "obsidian-input-" + Guid.NewGuid().ToString("N"));
        try
        {
            CheckFile();
            CheckControls();
            InputActions.Load();
            CheckButtons();
            CheckVectors();
            CheckAxesAndModifiers();
            CheckLifetime();
            SaveData.Folder = folder;
            CheckRebinding();
            CheckListening();
            CheckEditor();
            CheckKeyMap();
        }
        finally
        {
            for (int i = 0; i < GamePad.MaximumGamePadCount; i++) GameInput.OverrideGamePad(i, null);
            DebugScreen.ConsoleOpen = false;
            Input.keyboardState = keyboard;
            Input.mouseState = mouse;
            Input.mouseLastState = lastMouse;
            GameInput.Update(Dt);
            InputActions.Load();
            SaveData.Folder = saveFolder;
            try { Directory.Delete(folder, true); } catch { /* nothing saved */ }
        }
        Console.WriteLine("Input action checks passed.");
    }

    private static void CheckFile()
    {
        string path = GameInfo.ResolveContentFile(InputActionList.FileName);
        string defaults = InputActionList.ToJson(InputActionList.Defaults());
        Check(File.Exists(path) && File.ReadAllText(path) == defaults,
            "Content/System/InputActions.json ships the default actions exactly as Anvil writes them");
        var names = InputActionList.Read().Actions.Select(a => a.Name).ToArray();
        Check(names.SequenceEqual(new[] { "Move", "Look", "Jump", "Sprint", "Crouch", "Interact", "Attack", "Previous", "Next", "Pause" }),
            "the default list is Move, Look, Jump, Sprint, Crouch, Interact, Attack, Previous, Next, Pause");

        var data = new InputActionList.Data
        {
            Actions =
            {
                new InputActionDefinition("Zoom", InputActionType.Axis,
                    InputBinding.Composite1D("Keyboard/Q", "Keyboard/E"),
                    new InputBinding("Mouse/Scroll", 0.5f) { InvertX = true }),
            },
        };
        string json = InputActionList.ToJson(data);
        InputBinding back = InputActionList.FromJson(json).Actions[0].Bindings[1];
        Check(json.Contains("\"Type\": \"Axis\"") && json.Contains("\"Scale\": 0.5") && !json.Contains("\"PerSecond\"") &&
              !json.Contains("\"Path\": null") && json.Split("\"Scale\"").Length == 2,
            "the file stores types by name and leaves out default values (Scale 1, false flags, empty paths)");
        Check(back.SameAs(data.Actions[0].Bindings[1]) && InputActionList.FromJson(json).Actions[0].Bindings[0].IsComposite,
            "bindings round-trip through JSON");

        var messy = new InputActionList.Data
        {
            Actions =
            {
                new InputActionDefinition("  Fire ", InputActionType.Button, new InputBinding(" Mouse/LeftButton "), new InputBinding()),
                new InputActionDefinition("fire", InputActionType.Axis),
                new InputActionDefinition("", InputActionType.Button),
            },
        };
        var clean = InputActionList.Normalize(messy).Actions;
        Check(clean.Count == 1 && clean[0].Name == "Fire" && clean[0].Bindings.Count == 1 && clean[0].Bindings[0].Path == "Mouse/LeftButton",
            "saving trims names and paths and drops nameless actions, duplicate names and empty bindings");
    }

    private static void CheckControls()
    {
        Check(InputControls.Find("gamepad/a") == InputControls.Find("GamePad/A") && InputControls.Find("GamePad/A")!.Type == InputControlType.Button &&
              InputControls.Find("GamePad/LeftStick")!.Type == InputControlType.Vector2 && InputControls.Find("Mouse/Scroll")!.Type == InputControlType.Axis,
            "control paths resolve case-insensitively with their value type");
        Check(InputControls.Find("Keyboard/VolumeUp")?.Device == InputDevice.Keyboard && InputControls.Find("Keyboard/Nope") == null &&
              InputControls.Find(null!) == null, "any XNA key name binds; unknown paths don't");
        Check(InputControls.DisplayName("Keyboard/D1") == "1" && InputControls.DisplayName("Keyboard/PageUp") == "Page Up" &&
              InputControls.DisplayName("Mouse/LeftButton") == "Left Mouse" && InputControls.DisplayName("GamePad/RightTrigger") == "RT",
            "controls have prompt-friendly names");
        Check(InputControls.For(InputDevice.Keyboard).Count > 90 && InputControls.For(InputDevice.Mouse).Count == 11 &&
              InputControls.For(InputDevice.GamePad).Any(c => c.Path == "GamePad/RightStickUp"),
            "the editor lists the whole keyboard, the mouse and every gamepad control");
    }

    private static void CheckButtons()
    {
        InputAction jump = InputActions.Get("Jump");
        var probe = new Probe();
        Frame();
        Frame(XnaKeys.Space);
        Check(jump.WasPressed && jump.IsPressed && probe.WasActionPressed("Jump") && probe.IsActionDown("jump") &&
              GameInput.WasActionPressed("Jump") && jump.HeldTime == 0f && jump.ActiveDevice == InputDevice.Keyboard,
            "a key press fires its Button action (script helpers and GameInput, names case-insensitive)");
        Frame(XnaKeys.Space);
        Check(!jump.WasPressed && jump.IsPressed && Math.Abs(jump.HeldTime - Dt) < 1e-6f, "holding keeps it pressed and counts HeldTime");
        Frame();
        Check(jump.WasReleased && !jump.IsPressed && probe.WasActionReleased("Jump"), "letting go releases it");

        Pad(buttons: Buttons.A);
        Check(jump.WasPressed && jump.ActiveDevice == InputDevice.GamePad, "the gamepad binding fires the same action");
        Pad();

        InputAction attack = InputActions.Get("Attack");
        Pad(rightTrigger: 0.6f);
        bool pressed = attack.IsPressed && Math.Abs(attack.Value - 0.6f) < 1e-4f;
        Pad(rightTrigger: 0.45f);
        bool held = attack.IsPressed;
        Pad(rightTrigger: 0.3f);
        Check(pressed && held && !attack.IsPressed, "analog triggers press at 0.5 and release below 0.375, keeping the analog Value");
        Pad();
    }

    private static void CheckVectors()
    {
        InputAction move = InputActions.Get("Move");
        Frame(XnaKeys.W, XnaKeys.D);
        Check(Near(move.Vector, new Vector2(0.7071f, 0.7071f)) && Near(GameInput.GetVector("Move"), move.Vector) && move.IsPressed,
            "a W/A/S/D composite gives a 2D value, normalized on diagonals");
        Pad(leftStick: new Vector2(0.3f, 0f), keys: new[] { XnaKeys.D });
        Check(Near(move.Vector, Vector2.UnitX) && move.ActiveDevice == InputDevice.Keyboard, "the strongest binding wins (keys over a slight stick)");
        Pad(leftStick: new Vector2(0f, 0.9f));
        Check(Near(move.Vector, new Vector2(0, 0.9f)) && move.ActiveDevice == InputDevice.GamePad, "the left stick moves with its analog value");
        Pad();
        Check(move.Vector == Vector2.Zero && !move.IsPressed, "no input reads zero");

        InputAction look = InputActions.Get("Look");
        Frame(mouse: new MouseState(110, 96, 0, ButtonState.Released, ButtonState.Released, ButtonState.Pressed, ButtonState.Released, ButtonState.Released),
            lastMouse: new MouseState(100, 100, 0, ButtonState.Released, ButtonState.Released, ButtonState.Pressed, ButtonState.Released, ButtonState.Released));
        Check(Near(look.Vector, new Vector2(1.5f, 0.6f)), "Look: dragging with the right mouse button gives degrees (0.15 per pixel, +Y up)");
        Frame(mouse: new MouseState(130, 80, 0, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released),
            lastMouse: new MouseState(110, 96, 0, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released));
        Check(look.Vector == Vector2.Zero, "Look: moving the mouse without its Modifier does nothing");
        Pad(rightStick: new Vector2(1f, 0f));
        Check(Near(look.Vector, new Vector2(150f * Dt, 0)), "Look: the right stick turns 150 degrees per second (Per Second binding)");
        Pad();
        Frame(XnaKeys.Left);
        Check(Near(look.Vector, new Vector2(-120f * Dt, 0)), "Look: the Arrow Keys composite turns 120 degrees per second");
        Frame();
    }

    private static void CheckAxesAndModifiers()
    {
        InputActions.Apply(new InputActionList.Data
        {
            Actions =
            {
                new InputActionDefinition("Zoom", InputActionType.Axis,
                    InputBinding.Composite1D("Keyboard/Q", "Keyboard/E"), new InputBinding("Mouse/Scroll")),
                new InputActionDefinition("Quick Save", InputActionType.Button,
                    new InputBinding("Keyboard/S", modifier: "Keyboard/LeftControl")),
                new InputActionDefinition("Steer", InputActionType.Axis, new InputBinding("GamePad/LeftStickX") { InvertX = true }),
            },
        });
        Frame(XnaKeys.E);
        bool positive = Math.Abs(GameInput.GetAxis("Zoom") - 1f) < 1e-6f;
        Frame(XnaKeys.Q);
        bool negative = Math.Abs(GameInput.GetAxis("Zoom") + 1f) < 1e-6f;
        Frame(mouse: new MouseState(0, 0, 240, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released),
            lastMouse: new MouseState(0, 0, 0, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released));
        Check(positive && negative && Math.Abs(GameInput.GetAxis("Zoom") - 2f) < 1e-6f,
            "an Axis action reads a Negative/Positive key pair and the scroll wheel (notches)");
        Frame(XnaKeys.S);
        bool alone = GameInput.IsActionDown("Quick Save");
        Frame(XnaKeys.S, XnaKeys.LeftControl);
        Check(!alone && GameInput.WasActionPressed("Quick Save") && InputActions.Get("Quick Save").Bindings[0].DisplayText == "Left Ctrl + S",
            "a Modifier must be held for its binding (Ctrl+S)");
        Pad(leftStick: new Vector2(0.5f, 0));
        Check(Math.Abs(GameInput.GetAxis("Steer") + 0.5f) < 1e-4f, "Invert flips an axis");
        Pad();
        InputActions.Load();
    }

    private static void CheckLifetime()
    {
        InputAction jump = InputActions.Get("Jump");
        Frame(XnaKeys.Space);
        Frame();
        Frame();
        bool buffered = jump.WasPressedWithin(0.1f);
        jump.ConsumePress();
        Check(buffered && !jump.WasPressedWithin(0.1f) && !InputActions.Get("Jump").WasPressedWithin(10f),
            "WasPressedWithin buffers a recent press until ConsumePress");

        DebugScreen.ConsoleOpen = true;
        Frame(XnaKeys.Space);
        bool blocked = !jump.IsPressed;
        DebugScreen.ConsoleOpen = false;
        Frame();
        jump.Enabled = false;
        Frame(XnaKeys.Space);
        bool disabled = !jump.IsPressed;
        Frame();
        Check(blocked && disabled, "actions read released while the debug console is open or the action is disabled");

        InputAction missing = InputActions.Get("Teleport");
        Frame(XnaKeys.T);
        Check(!missing.Exists && !missing.IsPressed && !InputActions.Has("Teleport") && ReferenceEquals(missing, InputActions.Get("teleport")),
            "an unknown action name never fires and never throws");

        InputActions.Apply(new InputActionList.Data { Actions = { new InputActionDefinition("Teleport", InputActionType.Button, new InputBinding("Keyboard/T")) } });
        bool gone = !jump.Exists && InputActions.All.Count == 1;
        Frame();
        Frame(XnaKeys.T);
        Check(gone && missing.Exists && missing.WasPressed, "actions keep their instance across reloads: a removed one goes quiet, a missing one comes alive");
        InputActions.RequestReload();
        Frame();
        Check(ReferenceEquals(jump, InputActions.Get("Jump")) && jump.Exists && jump.Enabled && InputActions.All.Count == 10 && !missing.Exists,
            "RequestReload reads the project file on the next update (Anvil's Save) and re-enables actions");
    }

    private static void CheckRebinding()
    {
        InputAction jump = InputActions.Get("Jump");
        Check(!jump.IsOverridden && jump.BindingText(InputDevice.Keyboard) == "Space" && jump.BindingText(InputDevice.GamePad) == "A" &&
              jump.BindingText(InputDevice.Mouse) == "Space" && InputActions.Get("Move").BindingText(InputDevice.Keyboard) == "W/A/S/D" &&
              InputActions.Get("Look").BindingText(InputDevice.Mouse) == "Right Mouse + Mouse Delta",
            "BindingText shows the binding for a device (mouse and keyboard count as one)");

        Check(jump.Rebind("Keyboard/J") && jump.IsOverridden && jump.BindingText(InputDevice.Keyboard) == "J" &&
              jump.BindingText(InputDevice.GamePad) == "A" && !jump.Rebind("Keyboard/Nope"),
            "Rebind replaces the binding of the control's device");
        Frame(XnaKeys.Space);
        bool oldKey = jump.IsPressed;
        Frame(XnaKeys.J);
        Check(!oldKey && jump.WasPressed, "the rebound key fires and the old one doesn't");
        Frame();

        InputActions.Get("Move").SetBindingPart(0, InputCompositePart.Up, "Keyboard/Z");
        Check(InputActions.SaveBindings() && SaveData.Exists(InputActions.DefaultBindingsSave), "SaveBindings writes the player's rebinds with SaveData");
        InputActions.ResetBindings();
        bool reset = !jump.IsOverridden && jump.BindingText(InputDevice.Keyboard) == "Space";
        Check(reset && InputActions.LoadBindings() && jump.BindingText(InputDevice.Keyboard) == "J" &&
              InputActions.Get("Move").Bindings[0].Up == "Keyboard/Z" && !InputActions.Get("Crouch").IsOverridden,
            "ResetBindings restores the project's; LoadBindings brings the saved rebinds back");
        Check(!InputActions.LoadBindings("NoSuchSave"), "LoadBindings without a save changes nothing");
        InputActions.Load();
        Check(!jump.IsOverridden, "Load (Anvil's Stop) drops runtime rebinds");
    }

    private static void CheckListening()
    {
        Frame();
        Frame(XnaKeys.K);
        string key = InputActions.ListenForControl();
        Frame(XnaKeys.K);
        Check(key == "Keyboard/K" && InputActions.ListenForControl() == null, "ListenForControl reports the key that went down this frame");
        Frame(mouse: new MouseState(0, 0, 0, ButtonState.Released, ButtonState.Released, ButtonState.Pressed, ButtonState.Released, ButtonState.Released),
            lastMouse: new MouseState(0, 0, 0, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released));
        Check(InputActions.ListenForControl(InputDevice.Mouse) == "Mouse/RightButton" && InputActions.ListenForControl(InputDevice.Keyboard) == null,
            "ListenForControl detects mouse buttons and can be limited to one device");
        Frame();
        Pad(rightTrigger: 0.8f);
        Check(InputActions.ListenForControl(InputDevice.GamePad) == "GamePad/RightTrigger", "ListenForControl detects a trigger pulled past halfway");
        Pad();

        var now = new GamePadState(new GamePadThumbSticks(new Vector2(0, 0.9f), Vector2.Zero), new GamePadTriggers(), new GamePadButtons(), new GamePadDPad());
        var rest = new GamePadState(new GamePadThumbSticks(), new GamePadTriggers(), new GamePadButtons(), new GamePadDPad());
        Check(InputControls.DetectGamePad(now, rest) == "GamePad/LeftStickUp" && InputControls.DetectGamePad(now, now) == null &&
              InputControls.DetectGamePad(GamePadState.Default, rest) == null,
            "DetectGamePad (Anvil's Listen) reports stick directions once and ignores disconnected pads");
    }

    private static void CheckEditor()
    {
        var vm = new InputActionsViewModel(InputActionList.Defaults());
        Check(vm.Actions.Count == 10 && vm.SelectedAction?.Name == "Move" && vm.IsValid &&
              InputActionList.ToJson(vm.ToData()) == InputActionList.ToJson(InputActionList.Defaults()),
            "the Input Actions window loads and writes back the list unchanged");
        Check(vm.Actions[1].Bindings[0].Summary == "Right Mouse + Mouse Delta" && vm.Actions[1].Bindings[1].Summary == "Arrow Keys" &&
              vm.Actions[2].Usage.Contains("WasActionPressed(\"Jump\")") && vm.Actions[0].Usage.Contains("GetVector(\"Move\")"),
            "binding cards summarize their controls and each action shows its script call");

        vm.AddActionCommand.Execute(null);
        InputActionItemViewModel added = vm.SelectedAction!;
        Check(added.Name == "New Action" && vm.Actions.Count == 11 && vm.IsValid, "Add creates a uniquely named Button action");
        added.Name = "move";
        Check(!vm.IsValid && vm.ValidationMessage.Contains("move", StringComparison.OrdinalIgnoreCase), "duplicate names (case-insensitive) block Save");
        added.Name = "";
        Check(!vm.IsValid, "an empty name blocks Save");
        added.Name = "Dash";

        added.AddBindingCommand.Execute(null);
        InputControlSlotViewModel slot = added.Bindings[0].Slots[0];
        vm.StartListening(slot);
        Check(vm.IsListening && vm.ListeningPrompt.Contains("Dash"), "Listen waits for a control");
        vm.CompleteListening("GamePad/Y");
        Check(!vm.IsListening && slot.DeviceName == "GamePad" && slot.Path == "GamePad/Y" && added.Bindings[0].Summary == "Y",
            "a detected control fills the slot and switches its device");
        slot.Control = null;
        Check(slot.Path == "GamePad/Y", "a ComboBox's transient null doesn't clear a slot");
        slot.DeviceName = "Keyboard";
        Check(slot.Path == null && slot.Controls.Count > 90, "changing the device clears the control and lists the device's controls");
        slot.Control = InputControls.Find("Keyboard/LeftShift");
        added.Bindings[0].Modifier.SetPath("Keyboard/LeftAlt");
        InputActionDefinition dash = added.ToDefinition();
        Check(dash.Name == "Dash" && dash.Bindings.Single().Path == "Keyboard/LeftShift" && dash.Bindings[0].Modifier == "Keyboard/LeftAlt",
            "the edited action converts back to its definition");

        InputActionItemViewModel move = vm.Actions[0];
        move.Type = InputActionType.Axis;
        bool axisParts = move.Bindings[0].Slots.Select(s => s.Label).SequenceEqual(new[] { "Negative", "Positive" }) &&
                         move.Bindings[0].Slots[0].Path == "Keyboard/A" && !move.Bindings[0].ShowInvertY;
        move.Type = InputActionType.Vector2;
        InputBinding back = move.Bindings[0].ToBinding();
        Check(axisParts && back.Up == "Keyboard/W" && back.Down == "Keyboard/S" && back.Left == "Keyboard/A" && back.Right == "Keyboard/D",
            "switching a composite between Vector2 and Axis keeps its keys");
        move.AddCompositeCommand.Execute(null);
        Check(move.Bindings.Last().IsComposite && move.Bindings.Last().Slots.Count == 4, "+ Composite adds an Up/Down/Left/Right composite");

        vm.ResetToDefaultsCommand.Execute(null);
        Check(vm.Actions.Count == 10 && InputActionList.ToJson(vm.ToData()) == InputActionList.ToJson(InputActionList.Defaults()),
            "Reset to Defaults restores the default list");
    }

    private static void CheckKeyMap()
    {
        Check(KeyMap.ToXna(Key.D1) == XnaKeys.D1 && KeyMap.ToXna(Key.F12) == XnaKeys.F12 && KeyMap.ToXna(Key.NumPad5) == XnaKeys.NumPad5 &&
              KeyMap.ToXna(Key.OemMinus) == XnaKeys.OemMinus && KeyMap.ToXna(Key.PageDown) == XnaKeys.PageDown &&
              KeyMap.ToXna(Key.Delete) == XnaKeys.Delete && KeyMap.ToXna(Key.LeftCtrl) == XnaKeys.LeftControl &&
              KeyMap.ToXna(Key.Q) == XnaKeys.Q && KeyMap.ToXna(Key.None) == XnaKeys.None,
            "Anvil forwards the whole keyboard (digits, F-keys, numpad, punctuation, navigation)");
    }

    // One engine frame: these keys held, the mouse at rest unless given.
    private static void Frame(params XnaKeys[] keys) => Frame(keys, null, null);

    private static void Frame(XnaKeys[]? keys = null, MouseState? mouse = null, MouseState? lastMouse = null)
    {
        Input.keyboardState = new KeyboardState(keys ?? Array.Empty<XnaKeys>());
        Input.mouseLastState = lastMouse ?? _mouse;
        Input.mouseState = mouse ?? Input.mouseLastState;
        _mouse = Input.mouseState;
        GameInput.Update(Dt);
    }

    private static void Pad(Vector2 leftStick = default, Vector2 rightStick = default, float rightTrigger = 0f,
        Buttons buttons = 0, XnaKeys[]? keys = null)
    {
        GameInput.OverrideGamePad(0, new GamePadState(new GamePadThumbSticks(leftStick, rightStick),
            new GamePadTriggers(0f, rightTrigger), new GamePadButtons(buttons), new GamePadDPad()));
        Frame(keys, null, null);
    }

    private static bool Near(Vector2 a, Vector2 b) => Vector2.Distance(a, b) < 1e-3f;

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        Console.WriteLine("PASS: " + description);
    }
}
