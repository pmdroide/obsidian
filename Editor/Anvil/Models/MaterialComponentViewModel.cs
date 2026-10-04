using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Engine.Components;

namespace Anvil.Models;

public partial class MaterialComponentViewModel : ComponentViewModel
{
    public string[] Shaders { get; } = ["Standard", "Water"];
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsWater))] private int _shaderIndex;
    [ObservableProperty] private double _red = 1;
    [ObservableProperty] private double _green = 1;
    [ObservableProperty] private double _blue = 1;
    [ObservableProperty] private double _roughness = 0.25;
    [ObservableProperty] private double _metallic;
    [ObservableProperty] private double _emissiveStrength;
    [ObservableProperty] private bool _castShadows = true;
    [ObservableProperty] private double _opacity = 0.65;
    [ObservableProperty] private double _waveScale = 0.3;
    [ObservableProperty] private double _waveSpeed = 1;
    [ObservableProperty] private double _waveStrength = 0.2;
    public bool IsWater => ShaderIndex == (int)MaterialShader.Water;

    public MaterialComponentViewModel(SceneObjectViewModel owner) : base(owner, MaterialComponent.TypeId) { }

    partial void OnShaderIndexChanged(int value) => Push(c => ((MaterialComponent)c).Shader = (MaterialShader)Math.Clamp(value, 0, 1));
    partial void OnRedChanged(double value) => Push(c => ((MaterialComponent)c).Red = (float)value);
    partial void OnGreenChanged(double value) => Push(c => ((MaterialComponent)c).Green = (float)value);
    partial void OnBlueChanged(double value) => Push(c => ((MaterialComponent)c).Blue = (float)value);
    partial void OnRoughnessChanged(double value) => Push(c => ((MaterialComponent)c).Roughness = (float)value);
    partial void OnMetallicChanged(double value) => Push(c => ((MaterialComponent)c).Metallic = (float)value);
    partial void OnEmissiveStrengthChanged(double value) => Push(c => ((MaterialComponent)c).EmissiveStrength = (float)value);
    partial void OnCastShadowsChanged(bool value) => Push(c => ((MaterialComponent)c).CastShadows = value);
    partial void OnOpacityChanged(double value) => Push(c => ((MaterialComponent)c).Opacity = (float)value);
    partial void OnWaveScaleChanged(double value) => Push(c => ((MaterialComponent)c).WaveScale = (float)value);
    partial void OnWaveSpeedChanged(double value) => Push(c => ((MaterialComponent)c).WaveSpeed = (float)value);
    partial void OnWaveStrengthChanged(double value) => Push(c => ((MaterialComponent)c).WaveStrength = (float)value);

    [RelayCommand]
    private void WaterExample()
    {
        ShaderIndex = (int)MaterialShader.Water;
        Red = 0.05; Green = 0.35; Blue = 0.45;
        Roughness = 0.08; Opacity = 0.65;
        WaveScale = 0.3; WaveSpeed = 1; WaveStrength = 0.2;
    }

    public override void Apply(GameComponent component, bool freezeFields)
    {
        base.Apply(component, freezeFields);
        if (freezeFields) return;
        var material = (MaterialComponent)component;
        ShaderIndex = (int)material.Shader;
        Red = material.Red; Green = material.Green; Blue = material.Blue;
        Roughness = material.Roughness; Metallic = material.Metallic;
        EmissiveStrength = material.EmissiveStrength; CastShadows = material.CastShadows;
        Opacity = material.Opacity; WaveScale = material.WaveScale;
        WaveSpeed = material.WaveSpeed; WaveStrength = material.WaveStrength;
    }
}
