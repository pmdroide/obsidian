using System;
using System.Collections.Generic;
using System.Text.Json;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Engine.Scripting;
using XnaColor = Microsoft.Xna.Framework.Color;
using XnaVector2 = Microsoft.Xna.Framework.Vector2;
using XnaVector3 = Microsoft.Xna.Framework.Vector3;

namespace Anvil.Models;

/// <summary>
/// One serialized script field in the Inspector. Each kind has its own subclass and DataTemplate.
/// Edits go through <see cref="ScriptBehaviourComponentViewModel.PushField"/>; values coming from the
/// engine are set while the owner suppresses pushes, so they never echo back.
/// </summary>
public abstract partial class ScriptFieldViewModel : ObservableObject
{
    private readonly ScriptBehaviourComponentViewModel _component;
    public ScriptFieldInfo Info { get; }
    public string Name => Info.Name;
    public string DisplayName => Info.DisplayName;
    public string? Tooltip => Info.Tooltip;
    /// <summary>True when this attachment stores its own value instead of the script's default.</summary>
    [ObservableProperty] private bool _isOverridden;
    public IRelayCommand ResetCommand { get; }

    protected ScriptFieldViewModel(ScriptBehaviourComponentViewModel component, ScriptFieldInfo info)
    {
        _component = component;
        Info = info;
        ResetCommand = new RelayCommand(() => _component.ResetField(Name));
    }

    public static ScriptFieldViewModel Create(ScriptBehaviourComponentViewModel component, ScriptFieldInfo info) => info.Kind switch
    {
        ScriptFieldKind.Bool => new ScriptBoolFieldViewModel(component, info),
        ScriptFieldKind.Int or ScriptFieldKind.Float or ScriptFieldKind.Double when info.HasRange => new ScriptRangeFieldViewModel(component, info),
        ScriptFieldKind.Int or ScriptFieldKind.Float or ScriptFieldKind.Double => new ScriptNumberFieldViewModel(component, info),
        ScriptFieldKind.String => new ScriptTextFieldViewModel(component, info),
        ScriptFieldKind.Enum => new ScriptEnumFieldViewModel(component, info),
        ScriptFieldKind.Vector2 or ScriptFieldKind.Vector3 => new ScriptVectorFieldViewModel(component, info),
        ScriptFieldKind.Color => new ScriptColorFieldViewModel(component, info),
        _ => throw new ArgumentOutOfRangeException(nameof(info)),
    };

    /// <summary>Shows the attachment's value: <paramref name="stored"/> when it decodes, else the default.</summary>
    public void Load(IReadOnlyDictionary<string, JsonElement> stored)
    {
        bool has = ScriptFields.TryGetStored(Info, stored, out var json) && ScriptFields.TryDecode(Info, json, out _);
        _loading = true;
        try
        {
            IsOverridden = has;
            Show(ScriptFields.GetValue(Info, stored));
        }
        finally { _loading = false; }
    }

    private bool _loading;
    protected abstract void Show(object value);

    protected void Push(object value)
    {
        if (_loading) return;
        JsonElement json;
        try { json = ScriptFields.Encode(Info, value); }
        catch (ArgumentException) { return; } // transient control values (empty box, NaN)
        IsOverridden = true;
        _component.PushField(Name, json);
    }
}

public sealed partial class ScriptBoolFieldViewModel : ScriptFieldViewModel
{
    [ObservableProperty] private bool _value;
    public ScriptBoolFieldViewModel(ScriptBehaviourComponentViewModel c, ScriptFieldInfo i) : base(c, i) { }
    protected override void Show(object value) => Value = value is true;
    partial void OnValueChanged(bool value) => Push(value);
}

public partial class ScriptNumberFieldViewModel : ScriptFieldViewModel
{
    [ObservableProperty] private double? _value;
    public bool IsInteger => Info.Kind == ScriptFieldKind.Int;
    public string FormatString => IsInteger ? "0" : "0.###";

    public ScriptNumberFieldViewModel(ScriptBehaviourComponentViewModel c, ScriptFieldInfo i) : base(c, i) { }
    protected override void Show(object value) => Value = Convert.ToDouble(value);

    partial void OnValueChanged(double? value)
    {
        // An emptied NumericUpDown reports null; keep the last value until a number is typed.
        if (value is not double v || double.IsNaN(v)) return;
        if (IsInteger) Push((int)Math.Round(Math.Clamp(v, int.MinValue, int.MaxValue)));
        else Push(v);
    }
}

/// <summary>A [Range] number: a slider plus a number box. It has its own template so no slider ever clamps an unranged field.</summary>
public sealed class ScriptRangeFieldViewModel : ScriptNumberFieldViewModel
{
    public double Minimum => Info.Min;
    public double Maximum => Info.Max;
    public double SliderValue
    {
        get => Value ?? Minimum;
        set { if (Math.Abs(value - (Value ?? double.NaN)) > 1e-9) Value = value; }
    }

    public ScriptRangeFieldViewModel(ScriptBehaviourComponentViewModel c, ScriptFieldInfo i) : base(c, i)
    {
        PropertyChanged += (_, e) => { if (e.PropertyName == nameof(Value)) OnPropertyChanged(nameof(SliderValue)); };
    }
}

public sealed partial class ScriptTextFieldViewModel : ScriptFieldViewModel
{
    [ObservableProperty] private string? _value;
    public ScriptTextFieldViewModel(ScriptBehaviourComponentViewModel c, ScriptFieldInfo i) : base(c, i) { }
    protected override void Show(object value) => Value = value as string;
    partial void OnValueChanged(string? value) => Push(value ?? string.Empty);
}

public sealed partial class ScriptEnumFieldViewModel : ScriptFieldViewModel
{
    [ObservableProperty] private string? _value;
    public IReadOnlyList<string> Options => Info.EnumNames;
    public ScriptEnumFieldViewModel(ScriptBehaviourComponentViewModel c, ScriptFieldInfo i) : base(c, i) { }
    protected override void Show(object value) => Value = value?.ToString();

    partial void OnValueChanged(string? value)
    {
        // ComboBox can temporarily clear selection while its template is rebuilt.
        if (value != null) Push(value);
    }
}

public sealed partial class ScriptVectorFieldViewModel : ScriptFieldViewModel
{
    [ObservableProperty] private double _x;
    [ObservableProperty] private double _y;
    [ObservableProperty] private double _z;
    public bool HasZ => Info.Kind == ScriptFieldKind.Vector3;

    public ScriptVectorFieldViewModel(ScriptBehaviourComponentViewModel c, ScriptFieldInfo i) : base(c, i) { }

    private bool _showing;
    protected override void Show(object value)
    {
        _showing = true;
        try
        {
            if (value is XnaVector3 v3) { X = v3.X; Y = v3.Y; Z = v3.Z; }
            else if (value is XnaVector2 v2) { X = v2.X; Y = v2.Y; Z = 0; }
        }
        finally { _showing = false; }
    }

    partial void OnXChanged(double value) => PushVector();
    partial void OnYChanged(double value) => PushVector();
    partial void OnZChanged(double value) => PushVector();

    private void PushVector()
    {
        if (_showing) return;
        if (HasZ) Push(new XnaVector3((float)X, (float)Y, (float)Z));
        else Push(new XnaVector2((float)X, (float)Y));
    }
}

public sealed partial class ScriptColorFieldViewModel : ScriptFieldViewModel
{
    [ObservableProperty] private Color _value;
    public ScriptColorFieldViewModel(ScriptBehaviourComponentViewModel c, ScriptFieldInfo i) : base(c, i) { }

    protected override void Show(object value)
    {
        var c = value is XnaColor xna ? xna : XnaColor.White;
        Value = Color.FromArgb(c.A, c.R, c.G, c.B);
    }

    partial void OnValueChanged(Color value) => Push(new XnaColor(value.R, value.G, value.B, value.A));
}
