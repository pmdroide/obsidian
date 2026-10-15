using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Engine.Components;
using Engine.Logic;
using Anvil.Services;

namespace Anvil.Models;

/// <summary>Inspector of a Post Process Volume component (Source 2 look, phase 5.1).</summary>
public partial class PostProcessVolumeComponentViewModel : ComponentViewModel
{
    [ObservableProperty] private bool _infinite;
    [ObservableProperty] private double _blendDistance = 2;
    [ObservableProperty] private double _weight = 1;
    [ObservableProperty] private double _priority;
    [ObservableProperty] private string _profilePath = "";

    /// <summary>One row per post processing setting: whether this volume overrides it, and the value.</summary>
    public ObservableCollection<PostOverrideRow> Overrides { get; } = new();

    public PostProcessVolumeComponentViewModel(SceneObjectViewModel owner) : base(owner, PostProcessVolumeComponent.TypeId)
    {
        foreach (PropertyInfo property in PostProcessSettings.Settings)
        {
            if (property.GetCustomAttribute<System.Text.Json.Serialization.JsonIgnoreAttribute>() != null) continue;
            Overrides.Add(new PostOverrideRow(this, property));
        }
    }

    partial void OnInfiniteChanged(bool value) => Push(c => ((PostProcessVolumeComponent)c).Infinite = value);
    partial void OnBlendDistanceChanged(double value)
    {
        if (!double.IsFinite(value)) return;
        float v = (float)Math.Max(value, 0);
        Push(c => ((PostProcessVolumeComponent)c).BlendDistance = v);
    }
    partial void OnWeightChanged(double value)
    {
        if (!double.IsFinite(value)) return;
        float v = (float)Math.Clamp(value, 0, 1);
        Push(c => ((PostProcessVolumeComponent)c).Weight = v);
    }
    partial void OnPriorityChanged(double value)
    {
        if (!double.IsFinite(value)) return;
        int v = (int)Math.Round(value);
        Push(c => ((PostProcessVolumeComponent)c).Priority = v);
    }
    partial void OnProfilePathChanged(string value)
    {
        string path = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        Push(c => ((PostProcessVolumeComponent)c).ProfilePath = path);
    }

    internal void PushOverride(Action<PostProcessVolumeComponent> change) => Push(c => change((PostProcessVolumeComponent)c));

    public override void Apply(GameComponent component, bool freezeFields)
    {
        base.Apply(component, freezeFields);
        if (freezeFields) return;
        var volume = (PostProcessVolumeComponent)component;
        if (Infinite != volume.Infinite) Infinite = volume.Infinite;
        if (Math.Abs(BlendDistance - volume.BlendDistance) > 0.00001) BlendDistance = volume.BlendDistance;
        if (Math.Abs(Weight - volume.Weight) > 0.00001) Weight = volume.Weight;
        if (Math.Abs(Priority - volume.Priority) > 0.5) Priority = volume.Priority;
        string path = volume.ProfilePath ?? "";
        if (ProfilePath != path) ProfilePath = path;
        foreach (PostOverrideRow row in Overrides) row.Apply(volume);
    }
}

/// <summary>One overridable post processing setting of a Post Process Volume.</summary>
public partial class PostOverrideRow : ObservableObject
{
    private readonly PostProcessVolumeComponentViewModel _owner;
    private readonly PropertyInfo _property;
    private bool _applying;

    public string Name { get; }
    /// <summary>The setting's property name, shown as the label's tooltip.</summary>
    public string Setting { get; }
    public bool IsNumber { get; }
    public bool IsSwitch { get; }
    public bool IsChoice { get; }
    public bool IsText { get; }
    public bool IsColor { get; }
    public string[] Choices { get; } = Array.Empty<string>();

    [ObservableProperty] private bool _isOverridden;
    [ObservableProperty] private double _number;
    [ObservableProperty] private bool _switch;
    [ObservableProperty] private int _choice;
    [ObservableProperty] private string _text = "";
    [ObservableProperty] private Color _color = Colors.White;

    public PostOverrideRow(PostProcessVolumeComponentViewModel owner, PropertyInfo property)
    {
        _owner = owner;
        _property = property;
        Setting = property.Name;
        //"BloomStart" -> "Bloom Start"; long names get a short label that fits the Inspector
        Name = ShortNames.TryGetValue(property.Name, out string shortName) ? shortName
            : string.Concat(property.Name.Select((ch, i) => i > 0 && char.IsUpper(ch) && !char.IsUpper(property.Name[i - 1]) ? " " + ch : ch.ToString()));
        Type type = property.PropertyType;
        IsNumber = type == typeof(float);
        IsSwitch = type == typeof(bool);
        IsChoice = type.IsEnum;
        IsText = type == typeof(string);
        IsColor = type == typeof(Microsoft.Xna.Framework.Color);
        if (IsChoice) Choices = Enum.GetNames(type);
    }

    private static readonly System.Collections.Generic.Dictionary<string, string> ShortNames = new()
    {
        ["SCurveStrength"] = "S-Curve",
        ["ExactSrgb"] = "Exact sRGB",
        ["ChromaticAberration"] = "Chromatic Ab.",
        ["AutoExposureKey"] = "Exposure Key",
        ["BrightPixelPercent"] = "Bright Pixel %",
        ["BrightPixelTarget"] = "Bright Target",
        ["MinAverageLuminance"] = "Min Average",
        ["AutoExposureMin"] = "Min EV",
        ["AutoExposureMax"] = "Max EV",
        ["AdaptationSpeedUp"] = "Adapt Up",
        ["AdaptationSpeedDown"] = "Adapt Down",
        ["FogDensityScale"] = "Fog Density",
    };

    //Last value read from the engine: NumericUpDown works in decimal and hands back a rounded copy of floats like
    //1.1f when it binds, which must not count as an edit (it would tick the row)
    private double _engineNumber;

    public void Apply(PostProcessVolumeComponent volume)
    {
        _applying = true;
        try
        {
            bool overridden = volume.HasOverride(_property.Name);
            if (IsOverridden != overridden) IsOverridden = overridden;
            object value = _property.GetValue(volume.Profile ?? new PostProcessSettings());
            switch (value)
            {
                case float f: _engineNumber = f; if (Math.Abs(Number - f) > 0.00001) Number = f; break;
                case bool b when Switch != b: Switch = b; break;
                case Enum e when Choice != Convert.ToInt32(e): Choice = Convert.ToInt32(e); break;
                case string s when Text != s: Text = s; break;
                case Microsoft.Xna.Framework.Color c: var next = Color.FromRgb(c.R, c.G, c.B); if (Color != next) Color = next; break;
            }
        }
        finally { _applying = false; }
    }

    private void SetValue(object value)
    {
        if (_applying) return;
        PropertyInfo property = _property;
        _owner.PushOverride(v =>
        {
            v.Profile ??= new PostProcessSettings();
            property.SetValue(v.Profile, value);
            //Editing a value overrides it
            v.Overrides ??= new System.Collections.Generic.List<string>();
            if (!v.HasOverride(property.Name)) v.Overrides.Add(property.Name);
        });
        if (!IsOverridden) { _applying = true; IsOverridden = true; _applying = false; }
    }

    partial void OnIsOverriddenChanged(bool value)
    {
        if (_applying) return;
        string name = _property.Name;
        _owner.PushOverride(v =>
        {
            v.Overrides ??= new System.Collections.Generic.List<string>();
            v.Overrides.RemoveAll(o => string.Equals(o, name, StringComparison.OrdinalIgnoreCase));
            if (value) v.Overrides.Add(name);
        });
    }

    partial void OnNumberChanged(double value)
    {
        if (!IsNumber || !double.IsFinite(value) || Math.Abs(value - _engineNumber) < 0.00001) return;
        _engineNumber = value;
        SetValue((float)value);
    }
    partial void OnSwitchChanged(bool value) { if (IsSwitch) SetValue(value); }
    partial void OnChoiceChanged(int value) { if (IsChoice && value >= 0 && value < Choices.Length) SetValue(Enum.ToObject(_property.PropertyType, value)); }
    partial void OnTextChanged(string value) { if (IsText) SetValue(string.IsNullOrWhiteSpace(value) ? null : value.Trim()); }
    partial void OnColorChanged(Color value) { if (IsColor) SetValue(BridgeReconciler.ToXnaColor(value)); }
}
