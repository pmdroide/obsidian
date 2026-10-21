using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Engine.Components;

namespace Anvil.Models;

public partial class LodComponentViewModel : ComponentViewModel
{
    public ObservableCollection<LodLevelViewModel> Levels { get; } = new();
    [ObservableProperty] private double _cullDistance;
    public IRelayCommand AddLevelCommand { get; }

    // Set while Apply rebuilds or refreshes the rows, whose setters would otherwise push them back.
    private bool _applying;

    public LodComponentViewModel(SceneObjectViewModel owner) : base(owner, LodComponent.TypeId)
    {
        AddLevelCommand = new RelayCommand(() =>
        {
            // Each new level starts twice as far as the last one.
            double distance = Levels.Count > 0 ? Levels.Max(l => l.Distance) * 2 : 20;
            Levels.Add(new LodLevelViewModel(this, "", distance));
            PushLevels();
        });
    }

    partial void OnCullDistanceChanged(double value)
    {
        if (!double.IsFinite(value)) return;
        float distance = (float)Math.Max(value, 0);
        Push(c => ((LodComponent)c).CullDistance = distance);
    }

    internal void RemoveLevel(LodLevelViewModel level)
    {
        if (Levels.Remove(level)) PushLevels();
    }

    /// <summary>Sends the whole level list; the engine re-registers the gameobject's meshes.</summary>
    internal void PushLevels()
    {
        if (_applying) return;
        var levels = Levels.Select(l => new LodLevel { Model = l.CommittedModel, Distance = (float)l.CommittedDistance }).ToList();
        Push(c => ((LodComponent)c).Levels = levels);
    }

    /// <summary>The engine's model keys, plus <paramref name="current"/> when it is unknown (so it still shows).</summary>
    internal IReadOnlyList<string> ModelKeys(string current)
    {
        var keys = new List<string>();
        try
        {
            if (Owner.Bridge is { } bridge) keys.AddRange(bridge.AvailableModelKeys);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            // The engine thread is rebuilding the list (a model import); the next Apply fills it.
        }
        if (!string.IsNullOrEmpty(current) && !keys.Contains(current)) keys.Add(current);
        return keys;
    }

    public override void Apply(GameComponent component, bool freezeFields)
    {
        base.Apply(component, freezeFields);
        if (freezeFields) return;
        var lod = (LodComponent)component;
        if (Math.Abs(CullDistance - lod.CullDistance) > 0.0001) CullDistance = lod.CullDistance;

        var levels = lod.Levels ?? new List<LodLevel>();
        _applying = true;
        try
        {
            if (levels.Count != Levels.Count)
            {
                Levels.Clear();
                foreach (var level in levels)
                    Levels.Add(new LodLevelViewModel(this, level?.Model ?? "", level?.Distance ?? 20));
                return;
            }
            // Same rows: update in place so a focused field keeps its control.
            for (int i = 0; i < levels.Count; i++) Levels[i].Apply(levels[i]?.Model ?? "", levels[i]?.Distance ?? 20);
        }
        finally
        {
            _applying = false;
        }
    }
}

/// <summary>One row of the LOD component's inspector: a model key and the distance it starts at.</summary>
public partial class LodLevelViewModel : ObservableObject
{
    private readonly LodComponentViewModel _parent;
    [ObservableProperty] private string? _model;
    [ObservableProperty] private double _distance;
    [ObservableProperty] private IReadOnlyList<string> _modelKeys;
    public IRelayCommand RemoveCommand { get; }

    // Last valid values: what the engine gets for this row while another row is edited.
    internal string CommittedModel { get; private set; }
    internal double CommittedDistance { get; private set; }

    public LodLevelViewModel(LodComponentViewModel parent, string model, double distance)
    {
        _parent = parent;
        _modelKeys = parent.ModelKeys(model);
        _model = CommittedModel = model;
        _distance = CommittedDistance = distance;
        RemoveCommand = new RelayCommand(() => _parent.RemoveLevel(this));
    }

    internal void Apply(string model, double distance)
    {
        if (!string.IsNullOrEmpty(model) && !ModelKeys.Contains(model)) ModelKeys = _parent.ModelKeys(model);
        CommittedModel = model;
        CommittedDistance = distance;
        Model = model;
        if (Math.Abs(Distance - distance) > 0.0001) Distance = distance;
    }

    public void Clear()
    {
        Model = CommittedModel = "";
        _parent.PushLevels();
    }

    partial void OnModelChanged(string? value)
    {
        // The ComboBox clears its selection while its items change; that isn't a choice.
        if (string.IsNullOrEmpty(value)) return;
        CommittedModel = value;
        _parent.PushLevels();
    }

    partial void OnDistanceChanged(double value)
    {
        if (!double.IsFinite(value) || value <= 0) return;
        CommittedDistance = value;
        _parent.PushLevels();
    }
}
