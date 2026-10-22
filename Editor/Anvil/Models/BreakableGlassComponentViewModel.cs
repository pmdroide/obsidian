using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Engine.Components;
using Engine.Recources;

namespace Anvil.Models;

public partial class BreakableGlassComponentViewModel : ComponentViewModel
{
    [ObservableProperty] private double _crackSize = 0.381;
    [ObservableProperty] private double _holeRadius = 0.06;
    [ObservableProperty] private double _impactSpeed = 4;
    [ObservableProperty] private double _impactMomentum = 2;
    [ObservableProperty] private double _debrisLifetime = 15;
    [ObservableProperty] private double _hitTolerance = 2;
    [ObservableProperty] private bool _dropLooseShards = true;
    [ObservableProperty] private bool _distort = true;
    [ObservableProperty] private bool _solidShards = true;
    [ObservableProperty] private int _damagedMaterialIndex;
    [ObservableProperty] private int _edgeMaterialIndex;
    [ObservableProperty] private double _edgeWidth = 0.003;
    [ObservableProperty] private double _edgeTextureSize = 0.508;

    private const string BuiltIn = "Built-in";

    /// <summary>"Built-in" then every material asset in Content; only ever appended to, so indices stay valid.</summary>
    public ObservableCollection<string> MaterialChoices { get; } = new() { BuiltIn };

    public BreakableGlassComponentViewModel(SceneObjectViewModel owner) : base(owner, BreakableGlassComponent.TypeId)
    {
        foreach (string path in MaterialAsset.List()) MaterialChoices.Add(path);
    }

    // Index of a saved path in MaterialChoices (0 = built-in), adding a path the list doesn't have yet.
    private int IndexOf(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return 0;
        for (int i = 1; i < MaterialChoices.Count; i++)
            if (MaterialAsset.SamePath(MaterialChoices[i], path)) return i;
        MaterialChoices.Add(path);
        return MaterialChoices.Count - 1;
    }

    private string PathAt(int index) => index > 0 && index < MaterialChoices.Count ? MaterialChoices[index] : "";

    partial void OnDamagedMaterialIndexChanged(int value)
    {
        if (value < 0) return; // the ComboBox passes -1 while its items change
        string path = PathAt(value);
        Push(c => ((BreakableGlassComponent)c).DamagedMaterial = path);
    }

    partial void OnEdgeMaterialIndexChanged(int value)
    {
        if (value < 0) return;
        string path = PathAt(value);
        Push(c => ((BreakableGlassComponent)c).EdgeMaterial = path);
    }

    partial void OnEdgeWidthChanged(double value)
    {
        if (!double.IsFinite(value)) return;
        float width = (float)Math.Clamp(value, 0, 0.05);
        Push(c => ((BreakableGlassComponent)c).EdgeWidth = width);
    }

    partial void OnEdgeTextureSizeChanged(double value)
    {
        if (!double.IsFinite(value)) return;
        float size = (float)Math.Clamp(value, 0.01, 10);
        Push(c => ((BreakableGlassComponent)c).EdgeTextureSize = size);
    }

    partial void OnCrackSizeChanged(double value)
    {
        if (!double.IsFinite(value)) return;
        float size = (float)Math.Clamp(value, 0.05, 10);
        Push(c => ((BreakableGlassComponent)c).CrackSize = size);
    }

    partial void OnHoleRadiusChanged(double value)
    {
        if (!double.IsFinite(value)) return;
        float radius = (float)Math.Clamp(value, 0.01, 2);
        Push(c => ((BreakableGlassComponent)c).HoleRadius = radius);
    }

    partial void OnImpactSpeedChanged(double value)
    {
        if (!double.IsFinite(value)) return;
        float speed = (float)Math.Max(value, 0);
        Push(c => ((BreakableGlassComponent)c).ImpactSpeed = speed);
    }

    partial void OnImpactMomentumChanged(double value)
    {
        if (!double.IsFinite(value)) return;
        float momentum = (float)Math.Max(value, 0);
        Push(c => ((BreakableGlassComponent)c).ImpactMomentum = momentum);
    }

    partial void OnDebrisLifetimeChanged(double value)
    {
        if (!double.IsFinite(value)) return;
        float lifetime = (float)Math.Clamp(value, 0.1, 600);
        Push(c => ((BreakableGlassComponent)c).DebrisLifetime = lifetime);
    }

    partial void OnHitToleranceChanged(double value)
    {
        if (!double.IsFinite(value)) return;
        float tolerance = (float)Math.Clamp(value, 1, 5);
        Push(c => ((BreakableGlassComponent)c).HitTolerance = tolerance);
    }

    partial void OnDropLooseShardsChanged(bool value) => Push(c => ((BreakableGlassComponent)c).DropLooseShards = value);
    partial void OnDistortChanged(bool value) => Push(c => ((BreakableGlassComponent)c).Distort = value);
    partial void OnSolidShardsChanged(bool value) => Push(c => ((BreakableGlassComponent)c).SolidShards = value);

    public override void Apply(GameComponent component, bool freezeFields)
    {
        base.Apply(component, freezeFields);
        if (freezeFields) return;
        var glass = (BreakableGlassComponent)component;
        if (Math.Abs(CrackSize - glass.CrackSize) > 0.00001) CrackSize = glass.CrackSize;
        if (Math.Abs(HoleRadius - glass.HoleRadius) > 0.00001) HoleRadius = glass.HoleRadius;
        if (Math.Abs(ImpactSpeed - glass.ImpactSpeed) > 0.00001) ImpactSpeed = glass.ImpactSpeed;
        if (Math.Abs(ImpactMomentum - glass.ImpactMomentum) > 0.00001) ImpactMomentum = glass.ImpactMomentum;
        if (Math.Abs(DebrisLifetime - glass.DebrisLifetime) > 0.00001) DebrisLifetime = glass.DebrisLifetime;
        if (Math.Abs(HitTolerance - glass.HitTolerance) > 0.00001) HitTolerance = glass.HitTolerance;
        if (DropLooseShards != glass.DropLooseShards) DropLooseShards = glass.DropLooseShards;
        if (Distort != glass.Distort) Distort = glass.Distort;
        if (SolidShards != glass.SolidShards) SolidShards = glass.SolidShards;
        int damaged = IndexOf(glass.DamagedMaterial);
        if (DamagedMaterialIndex != damaged) DamagedMaterialIndex = damaged;
        int edge = IndexOf(glass.EdgeMaterial);
        if (EdgeMaterialIndex != edge) EdgeMaterialIndex = edge;
        if (Math.Abs(EdgeWidth - glass.EdgeWidth) > 0.00001) EdgeWidth = glass.EdgeWidth;
        if (Math.Abs(EdgeTextureSize - glass.EdgeTextureSize) > 0.00001) EdgeTextureSize = glass.EdgeTextureSize;
    }
}
