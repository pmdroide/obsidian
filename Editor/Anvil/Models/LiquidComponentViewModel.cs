using System;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Engine.Components;
using XnaColor = Microsoft.Xna.Framework.Color;

namespace Anvil.Models;

public partial class LiquidComponentViewModel : ComponentViewModel
{
    [ObservableProperty] private double _fill = 0.6;
    [ObservableProperty] private Color _liquidColor = Color.FromRgb(196, 120, 34);
    [ObservableProperty] private Color _foamColor = Color.FromRgb(245, 235, 210);
    [ObservableProperty] private double _opacity = 0.75;
    [ObservableProperty] private double _roughness = 0.1;
    [ObservableProperty] private double _emissive;
    [ObservableProperty] private double _settleTime = 20;
    [ObservableProperty] private double _foamSettleTime = 30;
    [ObservableProperty] private double _sensitivity = 1;
    [ObservableProperty] private double _lean = 1;
    [ObservableProperty] private double _wobbleScale = 0.02;
    [ObservableProperty] private double _wobbleSpeed = 1;
    [ObservableProperty] private double _surfaceTension = 1;
    [ObservableProperty] private double _waterLine = 1;
    [ObservableProperty] private double _foaminess = 1;
    [ObservableProperty] private double _fizz = 0.3;

    public LiquidComponentViewModel(SceneObjectViewModel owner) : base(owner, LiquidComponent.TypeId) { }

    /// <summary>Shakes the liquid in the viewport (a 3 m/s knock).</summary>
    [RelayCommand]
    private void Shake() => Push(c => ((LiquidComponent)c).Agitate(3f));

    partial void OnFillChanged(double value) => SetNumber(value, 0, 1, (c, v) => c.Fill = v);
    partial void OnOpacityChanged(double value) => SetNumber(value, 0, 1, (c, v) => c.Opacity = v);
    partial void OnRoughnessChanged(double value) => SetNumber(value, 0.02, 1, (c, v) => c.Roughness = v);
    partial void OnEmissiveChanged(double value) => SetNumber(value, 0, 20, (c, v) => c.Emissive = v);
    partial void OnSettleTimeChanged(double value) => SetNumber(value, 0.1, 600, (c, v) => c.SettleTime = v);
    partial void OnFoamSettleTimeChanged(double value) => SetNumber(value, 0.1, 600, (c, v) => c.FoamSettleTime = v);
    partial void OnSensitivityChanged(double value) => SetNumber(value, 0, 10, (c, v) => c.Sensitivity = v);
    partial void OnLeanChanged(double value) => SetNumber(value, 0, 5, (c, v) => c.Lean = v);
    partial void OnWobbleScaleChanged(double value) => SetNumber(value, 0, 0.5, (c, v) => c.WobbleScale = v);
    partial void OnWobbleSpeedChanged(double value) => SetNumber(value, 0, 10, (c, v) => c.WobbleSpeed = v);
    partial void OnSurfaceTensionChanged(double value) => SetNumber(value, 0, 5, (c, v) => c.SurfaceTension = v);
    partial void OnWaterLineChanged(double value) => SetNumber(value, 0, 5, (c, v) => c.WaterLine = v);
    partial void OnFoaminessChanged(double value) => SetNumber(value, 0, 4, (c, v) => c.Foaminess = v);
    partial void OnFizzChanged(double value) => SetNumber(value, 0, 4, (c, v) => c.Fizz = v);

    partial void OnLiquidColorChanged(Color value)
    {
        var color = new XnaColor(value.R, value.G, value.B, (byte)255);
        Push(c => ((LiquidComponent)c).LiquidColor = color);
    }

    partial void OnFoamColorChanged(Color value)
    {
        var color = new XnaColor(value.R, value.G, value.B, (byte)255);
        Push(c => ((LiquidComponent)c).FoamColor = color);
    }

    // NumericUpDown sends NaN while a field is being cleared.
    private void SetNumber(double value, double min, double max, Action<LiquidComponent, float> set)
    {
        if (!double.IsFinite(value)) return;
        float v = (float)Math.Clamp(value, min, max);
        Push(c => set((LiquidComponent)c, v));
    }

    public override void Apply(GameComponent component, bool freezeFields)
    {
        base.Apply(component, freezeFields);
        if (freezeFields) return;
        var liquid = (LiquidComponent)component;
        if (Math.Abs(Fill - liquid.Fill) > 0.00001) Fill = liquid.Fill;
        Color liquidColor = Color.FromRgb(liquid.LiquidColor.R, liquid.LiquidColor.G, liquid.LiquidColor.B);
        if (LiquidColor != liquidColor) LiquidColor = liquidColor;
        Color foamColor = Color.FromRgb(liquid.FoamColor.R, liquid.FoamColor.G, liquid.FoamColor.B);
        if (FoamColor != foamColor) FoamColor = foamColor;
        if (Math.Abs(Opacity - liquid.Opacity) > 0.00001) Opacity = liquid.Opacity;
        if (Math.Abs(Roughness - liquid.Roughness) > 0.00001) Roughness = liquid.Roughness;
        if (Math.Abs(Emissive - liquid.Emissive) > 0.00001) Emissive = liquid.Emissive;
        if (Math.Abs(SettleTime - liquid.SettleTime) > 0.00001) SettleTime = liquid.SettleTime;
        if (Math.Abs(FoamSettleTime - liquid.FoamSettleTime) > 0.00001) FoamSettleTime = liquid.FoamSettleTime;
        if (Math.Abs(Sensitivity - liquid.Sensitivity) > 0.00001) Sensitivity = liquid.Sensitivity;
        if (Math.Abs(Lean - liquid.Lean) > 0.00001) Lean = liquid.Lean;
        if (Math.Abs(WobbleScale - liquid.WobbleScale) > 0.00001) WobbleScale = liquid.WobbleScale;
        if (Math.Abs(WobbleSpeed - liquid.WobbleSpeed) > 0.00001) WobbleSpeed = liquid.WobbleSpeed;
        if (Math.Abs(SurfaceTension - liquid.SurfaceTension) > 0.00001) SurfaceTension = liquid.SurfaceTension;
        if (Math.Abs(WaterLine - liquid.WaterLine) > 0.00001) WaterLine = liquid.WaterLine;
        if (Math.Abs(Foaminess - liquid.Foaminess) > 0.00001) Foaminess = liquid.Foaminess;
        if (Math.Abs(Fizz - liquid.Fizz) > 0.00001) Fizz = liquid.Fizz;
    }
}
