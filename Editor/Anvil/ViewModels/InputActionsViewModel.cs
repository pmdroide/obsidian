using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Engine.Logic;

namespace Anvil.ViewModels;

/// <summary>
/// One control picker in the Input Actions window: a device, one of its controls, and Listen. A simple binding has
/// one ("Control"), a composite one per part ("Up", "Down", ...), and every binding has an optional "Modifier".
/// </summary>
public partial class InputControlSlotViewModel : ObservableObject
{
    public const string NoDevice = "None";
    private static readonly IReadOnlyList<InputControl> NoControls = Array.Empty<InputControl>();

    private readonly InputActionsViewModel _root;
    private bool _syncing;

    public string Label { get; }
    /// <summary>The modifier slot may be empty (device "None").</summary>
    public bool Optional { get; }
    public IReadOnlyList<string> Devices { get; }

    private string _deviceName = "Keyboard";
    private InputControl? _control;

    [ObservableProperty] private IReadOnlyList<InputControl> _controls = NoControls;

    // A ComboBox writes null while its items change or when the panel switches to another action;
    // only real picks count (the lists have no empty entry, and "None" is a device name).
    public string DeviceName
    {
        get => _deviceName;
        set
        {
            if (value == null || !SetProperty(ref _deviceName, value)) return;
            OnPropertyChanged(nameof(HasDevice));
            if (_syncing) return;
            _syncing = true;
            Controls = ControlsFor(value, null);
            SetControl(null);
            _syncing = false;
            Changed?.Invoke();
        }
    }

    public InputControl? Control
    {
        get => _control;
        set
        {
            if (value == null && !_syncing) return;
            if (SetControl(value) && !_syncing) Changed?.Invoke();
        }
    }

    public bool HasDevice => DeviceName != NoDevice;
    public string? Path => Control?.Path;

    /// <summary>Raised after the picked control changes (the binding's summary follows it).</summary>
    public event Action? Changed;

    public InputControlSlotViewModel(InputActionsViewModel root, string label, string? path, bool optional = false)
    {
        _root = root;
        Label = label;
        Optional = optional;
        Devices = optional
            ? new[] { NoDevice, "Keyboard", "Mouse", "GamePad" }
            : new[] { "Keyboard", "Mouse", "GamePad" };
        SetPath(path);
    }

    /// <summary>Picks the control at <paramref name="path"/> (switching device); null/unknown clears it.</summary>
    public void SetPath(string? path)
    {
        InputControl? control = InputControls.Find(path);
        _syncing = true;
        DeviceName = control != null ? InputControls.DeviceName(control.Device) : Optional ? NoDevice : DeviceName;
        Controls = ControlsFor(DeviceName, control);
        SetControl(control);
        _syncing = false;
        Changed?.Invoke();
    }

    private bool SetControl(InputControl? control)
    {
        if (!SetProperty(ref _control, control, nameof(Control))) return false;
        OnPropertyChanged(nameof(Path));
        return true;
    }

    [RelayCommand]
    private void Listen() => _root.StartListening(this);

    private static IReadOnlyList<InputControl> ControlsFor(string device, InputControl? current)
    {
        if (device == NoDevice) return NoControls;
        InputDevice d = device == "Mouse" ? InputDevice.Mouse : device == "GamePad" ? InputDevice.GamePad : InputDevice.Keyboard;
        IReadOnlyList<InputControl> listed = InputControls.For(d);
        // Keys outside the editor list (media keys...) still show when a binding uses one.
        if (current != null && !listed.Contains(current)) return listed.Append(current).ToList();
        return listed;
    }
}

/// <summary>One binding card: a simple control or a composite, plus modifier, scale, per second and invert.</summary>
public partial class InputBindingItemViewModel : ObservableObject
{
    private readonly InputActionItemViewModel _owner;
    private readonly InputActionsViewModel _root;
    // Composite parts the current action type doesn't show are kept, so switching the type back restores them.
    private readonly InputBinding _parts;

    public bool IsComposite { get; }
    public ObservableCollection<InputControlSlotViewModel> Slots { get; } = new();
    public InputControlSlotViewModel Modifier { get; }

    // decimal = NumericUpDown.Value's type, so the compiled bindings need no converter.
    [ObservableProperty] private decimal _scale = 1;
    [ObservableProperty] private bool _perSecond;
    [ObservableProperty] private bool _invertX;
    [ObservableProperty] private bool _invertY;

    public string Summary
    {
        get
        {
            string text = ToBinding().DisplayText;
            return string.IsNullOrEmpty(text) || text == "/" ? "Unbound" : text;
        }
    }

    public string Kind => IsComposite ? "Composite" : "Control";
    public bool ShowInvertY => _owner.IsVector2;
    public string InvertXLabel => _owner.IsVector2 ? "Invert X" : "Invert";

    public InputBindingItemViewModel(InputActionItemViewModel owner, InputActionsViewModel root, InputBinding binding)
    {
        _owner = owner;
        _root = root;
        IsComposite = binding.IsComposite;
        _parts = binding.Clone();
        _scale = (decimal)binding.Scale;
        _perSecond = binding.PerSecond;
        _invertX = binding.InvertX;
        _invertY = binding.InvertY;
        Modifier = new InputControlSlotViewModel(root, "Modifier", binding.Modifier, optional: true);
        Modifier.Changed += OnSlotChanged;
        if (!IsComposite) AddSlot("Control", binding.Path);
        else RebuildParts();
    }

    /// <summary>The composite's slots for the action type: Up/Down/Left/Right, or Negative/Positive.</summary>
    internal void RebuildParts()
    {
        OnPropertyChanged(nameof(ShowInvertY));
        OnPropertyChanged(nameof(InvertXLabel));
        if (!IsComposite) return;
        foreach (InputControlSlotViewModel slot in Slots)
        {
            slot.Changed -= OnSlotChanged;
            _parts.SetPart(Enum.Parse<InputCompositePart>(slot.Label), slot.Path);
        }
        Slots.Clear();
        if (_owner.IsVector2)
        {
            // A pair edited as an axis lands on Left/Right (and back).
            _parts.Left = _parts.Negative ?? _parts.Left;
            _parts.Right = _parts.Positive ?? _parts.Right;
            foreach (var part in new[] { InputCompositePart.Up, InputCompositePart.Down, InputCompositePart.Left, InputCompositePart.Right })
                AddSlot(part.ToString(), _parts.GetPart(part));
        }
        else
        {
            _parts.Negative = _parts.Left ?? _parts.Down ?? _parts.Negative;
            _parts.Positive = _parts.Right ?? _parts.Up ?? _parts.Positive;
            foreach (var part in new[] { InputCompositePart.Negative, InputCompositePart.Positive })
                AddSlot(part.ToString(), _parts.GetPart(part));
        }
        OnPropertyChanged(nameof(Summary));
    }

    public InputBinding ToBinding()
    {
        var b = new InputBinding
        {
            Modifier = Modifier.Path,
            Scale = (float)Scale,
            PerSecond = PerSecond,
            InvertX = InvertX,
            InvertY = InvertY && _owner.IsVector2,
        };
        if (!IsComposite) b.Path = Slots.FirstOrDefault()?.Path;
        else foreach (InputControlSlotViewModel slot in Slots) b.SetPart(Enum.Parse<InputCompositePart>(slot.Label), slot.Path);
        return b;
    }

    [RelayCommand]
    private void Remove() => _owner.Bindings.Remove(this);

    private void AddSlot(string label, string? path)
    {
        var slot = new InputControlSlotViewModel(_root, label, path);
        slot.Changed += OnSlotChanged;
        Slots.Add(slot);
    }

    private void OnSlotChanged() => OnPropertyChanged(nameof(Summary));
}

/// <summary>One action of the list: name, type and binding cards.</summary>
public partial class InputActionItemViewModel : ObservableObject
{
    private readonly InputActionsViewModel _root;

    public static IReadOnlyList<InputActionType> Types { get; } =
        new[] { InputActionType.Button, InputActionType.Axis, InputActionType.Vector2 };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Usage))]
    private string _name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVector2), nameof(CanAddComposite), nameof(CompositeLabel), nameof(TypeHint), nameof(Usage), nameof(TypeLabel))]
    private InputActionType _type;

    public ObservableCollection<InputBindingItemViewModel> Bindings { get; } = new();

    public bool IsVector2 => Type == InputActionType.Vector2;
    public bool CanAddComposite => Type != InputActionType.Button;
    public bool HasNoBindings => Bindings.Count == 0;
    public string CompositeLabel => IsVector2 ? "+ Composite (Up/Down/Left/Right)" : "+ Composite (Negative/Positive)";
    public string TypeLabel => Type.ToString();
    public string BindingCountLabel => Bindings.Count == 1 ? "1 binding" : $"{Bindings.Count} bindings";

    public string TypeHint => Type switch
    {
        InputActionType.Axis => "One value, usually -1..1: a stick axis, a trigger, the scroll wheel, or a Negative/Positive key pair.",
        InputActionType.Vector2 => "A 2D value with +Y up/forward: a stick, the D-pad, the mouse delta, or an Up/Down/Left/Right key composite.",
        _ => "Pressed or released. Triggers and stick directions count as pressed past halfway.",
    };

    public string Usage => Type switch
    {
        InputActionType.Axis => $"float value = GetAxis(\"{Name}\");",
        InputActionType.Vector2 => $"Vector2 value = GetVector(\"{Name}\");",
        _ => $"if (WasActionPressed(\"{Name}\")) {{ ... }}   // or IsActionDown(\"{Name}\")",
    };

    public InputActionItemViewModel(InputActionsViewModel root, InputActionDefinition definition)
    {
        _root = root;
        _name = definition.Name;
        _type = definition.Type;
        foreach (InputBinding b in definition.Bindings) Bindings.Add(new InputBindingItemViewModel(this, root, b));
        Bindings.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasNoBindings));
            OnPropertyChanged(nameof(BindingCountLabel));
        };
    }

    partial void OnNameChanged(string value) => _root.Validate();

    partial void OnTypeChanged(InputActionType value)
    {
        foreach (InputBindingItemViewModel b in Bindings) b.RebuildParts();
    }

    [RelayCommand]
    private void AddBinding() => Bindings.Add(new InputBindingItemViewModel(this, _root, new InputBinding()));

    [RelayCommand]
    private void AddComposite() => Bindings.Add(new InputBindingItemViewModel(this, _root,
        IsVector2 ? InputBinding.Composite2D("Keyboard/W", "Keyboard/S", "Keyboard/A", "Keyboard/D")
                  : InputBinding.Composite1D("Keyboard/Q", "Keyboard/E")));

    public InputActionDefinition ToDefinition() => new()
    {
        Name = Name?.Trim() ?? "",
        Type = Type,
        Bindings = Bindings.Select(b => b.ToBinding()).Where(b => !b.IsEmpty).ToList(),
    };
}

/// <summary>
/// Backs Anvil's Input Actions window (Window > Input Actions): the project's named actions and their keyboard,
/// mouse and gamepad bindings. Nothing is written until <see cref="Apply"/> (Save); the running engine then reloads
/// the list (<see cref="InputActions.RequestReload"/>), also during Play.
/// </summary>
public partial class InputActionsViewModel : ViewModelBase
{
    public ObservableCollection<InputActionItemViewModel> Actions { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedAction), nameof(HasNoSelectedAction))]
    [NotifyCanExecuteChangedFor(nameof(RemoveActionCommand), nameof(DuplicateActionCommand), nameof(MoveActionUpCommand), nameof(MoveActionDownCommand))]
    private InputActionItemViewModel? _selectedAction;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsListening), nameof(ListeningPrompt))]
    private InputControlSlotViewModel? _listeningSlot;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid), nameof(HasValidationMessage))]
    private string _validationMessage = "";

    public bool HasSelectedAction => SelectedAction != null;
    public bool HasNoSelectedAction => SelectedAction == null;
    public bool IsListening => ListeningSlot != null;
    public bool IsValid => ValidationMessage.Length == 0;
    public bool HasValidationMessage => !IsValid;

    public string ListeningPrompt => ListeningSlot == null ? "" :
        $"Press a key, mouse button or gamepad control for {(ListeningSlot.Label == "Control" ? SelectedAction?.Name : $"{SelectedAction?.Name} > {ListeningSlot.Label}")}";

    public InputActionsViewModel() : this(InputActionList.Read()) { }

    public InputActionsViewModel(InputActionList.Data data)
    {
        Load(data);
        Actions.CollectionChanged += (_, _) => Validate();
    }

    private void Load(InputActionList.Data data)
    {
        Actions.Clear();
        foreach (InputActionDefinition d in InputActionList.Normalize(data).Actions)
            Actions.Add(new InputActionItemViewModel(this, d));
        SelectedAction = Actions.FirstOrDefault();
        Validate();
    }

    [RelayCommand]
    private void AddAction()
    {
        var action = new InputActionItemViewModel(this, new InputActionDefinition(UniqueName("New Action"), InputActionType.Button));
        Actions.Add(action);
        SelectedAction = action;
    }

    private bool HasSelected => SelectedAction != null;

    [RelayCommand(CanExecute = nameof(HasSelected))]
    private void DuplicateAction()
    {
        if (SelectedAction is not { } source) return;
        InputActionDefinition copy = source.ToDefinition();
        copy.Name = UniqueName(copy.Name + " Copy");
        var action = new InputActionItemViewModel(this, copy);
        Actions.Insert(Actions.IndexOf(source) + 1, action);
        SelectedAction = action;
    }

    [RelayCommand(CanExecute = nameof(HasSelected))]
    private void RemoveAction()
    {
        if (SelectedAction is not { } action) return;
        int index = Actions.IndexOf(action);
        Actions.Remove(action);
        SelectedAction = Actions.Count == 0 ? null : Actions[Math.Min(index, Actions.Count - 1)];
    }

    private bool CanMoveUp => SelectedAction != null && Actions.IndexOf(SelectedAction) > 0;
    private bool CanMoveDown => SelectedAction != null && Actions.IndexOf(SelectedAction) < Actions.Count - 1;

    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private void MoveActionUp() => MoveSelected(-1);

    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private void MoveActionDown() => MoveSelected(1);

    private void MoveSelected(int delta)
    {
        if (SelectedAction is not { } action) return;
        int index = Actions.IndexOf(action);
        Actions.Move(index, index + delta);
        SelectedAction = action;
        MoveActionUpCommand.NotifyCanExecuteChanged();
        MoveActionDownCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Replaces the list with the engine's defaults (Move, Look, Jump...). Saved only with Save.</summary>
    [RelayCommand]
    private void ResetToDefaults() => Load(InputActionList.Defaults());

    // -------- Listening ("press a key") --------

    public void StartListening(InputControlSlotViewModel slot) => ListeningSlot = slot;

    /// <summary>Gives the listening slot the control the window detected (a binding path), and stops listening.</summary>
    public void CompleteListening(string path)
    {
        ListeningSlot?.SetPath(path);
        ListeningSlot = null;
    }

    [RelayCommand]
    public void CancelListening() => ListeningSlot = null;

    // -------- Save --------

    /// <summary>Names must be present and unique (case-insensitive): scripts find actions by name.</summary>
    public void Validate()
    {
        var names = Actions.Select(a => a.Name?.Trim() ?? "").ToList();
        if (names.Any(n => n.Length == 0)) ValidationMessage = "Every action needs a name.";
        else if (names.GroupBy(n => n, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1) is { } dup)
            ValidationMessage = $"Two actions are called \"{dup.Key}\".";
        else ValidationMessage = "";
    }

    public InputActionList.Data ToData() => new() { Actions = Actions.Select(a => a.ToDefinition()).ToList() };

    /// <summary>Writes Content/System/InputActions.json and has the running engine reload it. UI thread.</summary>
    public void Apply()
    {
        InputActionList.Save(ToData());
        InputActions.RequestReload();
    }

    private string UniqueName(string baseName)
    {
        string name = baseName;
        for (int i = 2; Actions.Any(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)); i++)
            name = $"{baseName} {i}";
        return name;
    }
}
