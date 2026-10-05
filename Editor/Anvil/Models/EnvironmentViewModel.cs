using System;
using System.IO;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Engine.Editor;
using Engine.Logic;

namespace Anvil.Models;

public partial class EnvironmentViewModel : ObservableObject
{
    private IEditorBridge? _bridge;
    private bool _suppress;
    public string[] SkyModes { get; } = { "Custom skybox", "Day / night cycle" };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustomSkybox), nameof(IsDayNightCycle))]
    private int _selectedSkyMode;
    [ObservableProperty] private double _timeOfDay = 12;
    [ObservableProperty] private double _cycleDurationMinutes = 10;
    [ObservableProperty] private string _skyboxName = "Default skybox";
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChooseSkybox))]
    [NotifyCanExecuteChangedFor(nameof(ResetSkyboxCommand))]
    private bool _isReady;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChooseSkybox))]
    [NotifyCanExecuteChangedFor(nameof(ResetSkyboxCommand))]
    private bool _isBusy;

    public bool IsCustomSkybox => SelectedSkyMode == 0;
    public bool IsDayNightCycle => SelectedSkyMode == 1;
    public bool CanChooseSkybox => IsReady && !IsBusy;

    public void AttachBridge(IEditorBridge bridge)
    {
        if (_bridge != null) return;
        _bridge = bridge;
        IsReady = true;
        bridge.SceneChanged += () => Dispatcher.UIThread.Post(Reload);
        Reload();
    }

    private void Reload()
    {
        if (_bridge == null) return;
        var settings = _bridge.GetEnvironmentSettings();
        _suppress = true;
        try
        {
            SelectedSkyMode = settings.DayNightCycle ? 1 : 0;
            TimeOfDay = settings.TimeOfDay;
            CycleDurationMinutes = settings.CycleDurationMinutes;
            SkyboxName = string.IsNullOrEmpty(settings.SkyboxPath) ? "Default skybox" : Path.GetFileName(settings.SkyboxPath);
            StatusMessage = string.Empty;
        }
        finally { _suppress = false; }
    }

    private void Push(Action<EnvironmentSettings> change)
    {
        if (!_suppress) _bridge?.EnqueueMutateEnvironment(change);
    }

    partial void OnSelectedSkyModeChanged(int value) => Push(s => s.DayNightCycle = value == 1);
    partial void OnTimeOfDayChanged(double value) => Push(s => s.TimeOfDay = (float)value);
    partial void OnCycleDurationMinutesChanged(double value) => Push(s => s.CycleDurationMinutes = (float)value);

    public void ImportSkybox(string path)
    {
        if (_bridge == null || IsBusy) return;
        IsBusy = true;
        StatusMessage = "Loading skybox…";
        _bridge.EnqueueImportSkybox(path, error => Dispatcher.UIThread.Post(() =>
        {
            IsBusy = false;
            if (error != null) StatusMessage = error;
            else
            {
                Reload();
                StatusMessage = "Skybox applied.";
            }
        }));
    }

    [RelayCommand(CanExecute = nameof(CanChooseSkybox))]
    private void ResetSkybox()
    {
        Push(s => { s.SkyboxPath = null; s.DayNightCycle = false; });
        _suppress = true;
        try { SelectedSkyMode = 0; SkyboxName = "Default skybox"; StatusMessage = string.Empty; }
        finally { _suppress = false; }
    }
}
