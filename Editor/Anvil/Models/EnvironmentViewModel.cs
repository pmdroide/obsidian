using System;
using System.IO;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Engine.Editor;
using Engine.Logic;
using Engine.Voxels;

namespace Anvil.Models;

public partial class EnvironmentViewModel : ObservableObject
{
    private IEditorBridge? _bridge;
    private bool _suppress;
    public string[] SkyModes { get; } = { "Custom skybox", "Day / night cycle" };
    // Same order as WeatherTypeValues.
    public string[] WeatherTypes { get; } = { "None", "Rain", "Sandstorm", "Snow" };
    private static readonly WeatherType[] WeatherTypeValues =
        { WeatherType.None, WeatherType.Rain, WeatherType.Sandstorm, WeatherType.Snow };
    // Same order as VoxelTerrainValues.
    public string[] VoxelTerrains { get; } = { "Flat", "Hills", "Mountains", "Islands" };
    private static readonly VoxelTerrain[] VoxelTerrainValues =
        { VoxelTerrain.Flat, VoxelTerrain.Hills, VoxelTerrain.Mountains, VoxelTerrain.Islands };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustomSkybox), nameof(IsDayNightCycle))]
    private int _selectedSkyMode;
    [ObservableProperty] private double _timeOfDay = 12;
    [ObservableProperty] private double _cycleDurationMinutes = 10;
    [ObservableProperty] private double _cloudCoverage = 0.45;
    [ObservableProperty] private double _cloudSpeed = 1;
    [ObservableProperty] private Color _daySkyColor;
    [ObservableProperty] private Color _dayHorizonColor;
    [ObservableProperty] private Color _sunsetColor;
    [ObservableProperty] private Color _nightSkyColor;
    [ObservableProperty] private Color _nightHorizonColor;
    [ObservableProperty] private double _sunBrightness = 1;
    [ObservableProperty] private double _sunSize = 1;
    [ObservableProperty] private double _moonBrightness = 1;
    [ObservableProperty] private double _moonSize = 1;
    [ObservableProperty] private double _starBrightness = 1;
    [ObservableProperty] private double _dayExposure = -1;
    [ObservableProperty] private double _nightExposure = -2.5;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWeather))]
    private int _selectedWeather;
    [ObservableProperty] private double _weatherIntensity = 0.7;
    [ObservableProperty] private double _weatherHaze = 0.6;
    [ObservableProperty] private double _windSpeed = 4;
    [ObservableProperty] private double _windDirection = 45;
    [ObservableProperty] private double _groundFog = 0.6;
    [ObservableProperty] private double _groundFogHeight = 1.2;
    [ObservableProperty] private double _groundFogBase;
    [ObservableProperty] private double _surfaceDrops = 1;
    [ObservableProperty] private double _fogDensity = 0.03;
    [ObservableProperty] private double _fogAbsorption = 0.1;
    [ObservableProperty] private double _fogSunScatter = 0.1;
    [ObservableProperty] private double _fogLightScatter = 1.5;
    [ObservableProperty] private double _fogIndirectScatter = 0.1;
    [ObservableProperty] private double _fogAnisotropy = 0.45;
    [ObservableProperty] private double _fogStartDistance = 30;
    [ObservableProperty] private double _fogFullDistance = 400;
    [ObservableProperty] private double _fogNearDensity = 0;
    [ObservableProperty] private double _fogSkyStrength = 0.1;
    [ObservableProperty] private double _fogSkyShaftDistance = 200;
    [ObservableProperty] private double _maxAutoExposure = 3;
    [ObservableProperty] private bool _voxelsEnabled;
    [ObservableProperty] private int _selectedVoxelTerrain = 1;
    [ObservableProperty] private decimal _voxelSeed = 1337;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VoxelSummary))]
    private double _voxelWorldSize = 128;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VoxelSummary))]
    private double _voxelWorldHeight = 64;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VoxelSummary))]
    private double _voxelBlockSize = 1;
    [ObservableProperty] private decimal _voxelBaseHeight = -24;
    [ObservableProperty] private bool _voxelCaves = true;
    [ObservableProperty] private double _voxelTrees = 0.5;
    [ObservableProperty] private double _voxelSeaLevel = 0.3;
    [ObservableProperty] private bool _voxelCollision = true;
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
    public bool HasWeather => SelectedWeather > 0;

    /// <summary>The voxel world's size in blocks and metres.</summary>
    public string VoxelSummary
    {
        get
        {
            int size = VoxelWorldSettings.ClampSize((int)Math.Round(VoxelWorldSize));
            int height = VoxelWorldSettings.ClampHeight((int)Math.Round(VoxelWorldHeight));
            float block = VoxelWorldSettings.ClampBlockSize((float)VoxelBlockSize);
            return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0} × {0} × {1} blocks  ·  {2:0.#} × {2:0.#} × {3:0.#} m", size, height, size * block, height * block);
        }
    }

    public void AttachBridge(IEditorBridge bridge)
    {
        if (_bridge != null) return;
        _bridge = bridge;
        IsReady = true;
        bridge.SceneChanged += () => Dispatcher.UIThread.Post(Reload);
        // Stop restores the environment scripts changed during Play.
        bridge.ModeChanged += _ => Dispatcher.UIThread.Post(Reload);
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
            CloudCoverage = settings.CloudCoverage;
            CloudSpeed = settings.CloudSpeed;
            ApplySkyLook(settings);
            SelectedWeather = Math.Max(0, Array.IndexOf(WeatherTypeValues, settings.Weather));
            WeatherIntensity = settings.WeatherIntensity;
            WeatherHaze = settings.WeatherHaze;
            WindSpeed = settings.WindSpeed;
            WindDirection = settings.WindDirection;
            GroundFog = settings.GroundFog;
            GroundFogHeight = settings.GroundFogHeight;
            GroundFogBase = settings.GroundFogBase;
            SurfaceDrops = settings.SurfaceDrops;
            FogDensity = settings.FogDensity;
            FogAbsorption = settings.FogAbsorption;
            FogSunScatter = settings.FogSunScatter;
            FogLightScatter = settings.FogLightScatter;
            FogIndirectScatter = settings.FogIndirectScatter;
            FogAnisotropy = settings.FogAnisotropy;
            FogStartDistance = settings.FogStartDistance;
            FogFullDistance = settings.FogFullDistance;
            FogNearDensity = settings.FogNearDensity;
            FogSkyStrength = settings.FogSkyStrength;
            FogSkyShaftDistance = settings.FogSkyShaftDistance;
            MaxAutoExposure = settings.MaxAutoExposure;
            VoxelsEnabled = settings.Voxels;
            SelectedVoxelTerrain = Math.Max(0, Array.IndexOf(VoxelTerrainValues, settings.VoxelTerrain));
            VoxelSeed = settings.VoxelSeed;
            VoxelWorldSize = settings.VoxelWorldSize;
            VoxelWorldHeight = settings.VoxelWorldHeight;
            VoxelBlockSize = settings.VoxelBlockSize;
            VoxelBaseHeight = (decimal)settings.VoxelBaseHeight;
            VoxelCaves = settings.VoxelCaves;
            VoxelTrees = settings.VoxelTrees;
            VoxelSeaLevel = settings.VoxelSeaLevel;
            VoxelCollision = settings.VoxelCollision;
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
    partial void OnCloudCoverageChanged(double value) => Push(s => s.CloudCoverage = (float)value);
    partial void OnCloudSpeedChanged(double value) => Push(s => s.CloudSpeed = (float)value);
    partial void OnDaySkyColorChanged(Color value) => Push(s => s.DaySkyColor = ToXna(value));
    partial void OnDayHorizonColorChanged(Color value) => Push(s => s.DayHorizonColor = ToXna(value));
    partial void OnSunsetColorChanged(Color value) => Push(s => s.SunsetColor = ToXna(value));
    partial void OnNightSkyColorChanged(Color value) => Push(s => s.NightSkyColor = ToXna(value));
    partial void OnNightHorizonColorChanged(Color value) => Push(s => s.NightHorizonColor = ToXna(value));
    partial void OnSunBrightnessChanged(double value) => Push(s => s.SunBrightness = (float)value);
    partial void OnSunSizeChanged(double value) => Push(s => s.SunSize = (float)value);
    partial void OnMoonBrightnessChanged(double value) => Push(s => s.MoonBrightness = (float)value);
    partial void OnMoonSizeChanged(double value) => Push(s => s.MoonSize = (float)value);
    partial void OnStarBrightnessChanged(double value) => Push(s => s.StarBrightness = (float)value);
    partial void OnDayExposureChanged(double value) => Push(s => s.DayExposure = (float)value);
    partial void OnNightExposureChanged(double value) => Push(s => s.NightExposure = (float)value);
    partial void OnSelectedWeatherChanged(int value)
    {
        // The ComboBox reports -1 while its items are being replaced.
        if (value >= 0 && value < WeatherTypeValues.Length) Push(s => s.Weather = WeatherTypeValues[value]);
    }
    partial void OnWeatherIntensityChanged(double value) => Push(s => s.WeatherIntensity = (float)value);
    partial void OnWeatherHazeChanged(double value) => Push(s => s.WeatherHaze = (float)value);
    partial void OnWindSpeedChanged(double value) => Push(s => s.WindSpeed = (float)value);
    partial void OnWindDirectionChanged(double value) => Push(s => s.WindDirection = (float)value);
    partial void OnGroundFogChanged(double value) => Push(s => s.GroundFog = (float)value);
    partial void OnGroundFogHeightChanged(double value) => Push(s => s.GroundFogHeight = (float)value);
    partial void OnGroundFogBaseChanged(double value) => Push(s => s.GroundFogBase = (float)value);
    partial void OnSurfaceDropsChanged(double value) => Push(s => s.SurfaceDrops = (float)value);
    partial void OnFogDensityChanged(double value) => Push(s => s.FogDensity = (float)value);
    partial void OnFogAbsorptionChanged(double value) => Push(s => s.FogAbsorption = (float)value);
    partial void OnFogSunScatterChanged(double value) => Push(s => s.FogSunScatter = (float)value);
    partial void OnFogLightScatterChanged(double value) => Push(s => s.FogLightScatter = (float)value);
    partial void OnFogIndirectScatterChanged(double value) => Push(s => s.FogIndirectScatter = (float)value);
    partial void OnFogAnisotropyChanged(double value) => Push(s => s.FogAnisotropy = (float)value);
    partial void OnFogStartDistanceChanged(double value) => Push(s => s.FogStartDistance = (float)value);
    partial void OnFogFullDistanceChanged(double value) => Push(s => s.FogFullDistance = (float)value);
    partial void OnFogNearDensityChanged(double value) => Push(s => s.FogNearDensity = (float)value);
    partial void OnFogSkyStrengthChanged(double value) => Push(s => s.FogSkyStrength = (float)value);
    partial void OnFogSkyShaftDistanceChanged(double value) => Push(s => s.FogSkyShaftDistance = (float)value);
    partial void OnMaxAutoExposureChanged(double value) => Push(s => s.MaxAutoExposure = (float)value);
    partial void OnVoxelsEnabledChanged(bool value) => Push(s => s.Voxels = value);
    partial void OnSelectedVoxelTerrainChanged(int value)
    {
        // The ComboBox reports -1 while its items are being replaced.
        if (value >= 0 && value < VoxelTerrainValues.Length) Push(s => s.VoxelTerrain = VoxelTerrainValues[value]);
    }
    partial void OnVoxelSeedChanged(decimal value) =>
        Push(s => s.VoxelSeed = (int)Math.Clamp(Math.Round(value), int.MinValue, int.MaxValue));
    partial void OnVoxelWorldSizeChanged(double value) => Push(s => s.VoxelWorldSize = (int)Math.Round(value));
    partial void OnVoxelWorldHeightChanged(double value) => Push(s => s.VoxelWorldHeight = (int)Math.Round(value));
    partial void OnVoxelBlockSizeChanged(double value) => Push(s => s.VoxelBlockSize = (float)value);
    partial void OnVoxelBaseHeightChanged(decimal value) => Push(s => s.VoxelBaseHeight = (float)value);
    partial void OnVoxelCavesChanged(bool value) => Push(s => s.VoxelCaves = value);
    partial void OnVoxelTreesChanged(double value) => Push(s => s.VoxelTrees = (float)value);
    partial void OnVoxelSeaLevelChanged(double value) => Push(s => s.VoxelSeaLevel = (float)value);
    partial void OnVoxelCollisionChanged(bool value) => Push(s => s.VoxelCollision = value);

    /// <summary>Restores the default (outdoor) volumetric fog and auto exposure cap of a new scene.</summary>
    [RelayCommand]
    private void ResetFog()
    {
        var d = new EnvironmentSettings();
        FogDensity = d.FogDensity; FogAbsorption = d.FogAbsorption; FogSunScatter = d.FogSunScatter;
        FogLightScatter = d.FogLightScatter; FogIndirectScatter = d.FogIndirectScatter; FogAnisotropy = d.FogAnisotropy; FogStartDistance = d.FogStartDistance;
        FogFullDistance = d.FogFullDistance; FogNearDensity = d.FogNearDensity; FogSkyStrength = d.FogSkyStrength;
        FogSkyShaftDistance = d.FogSkyShaftDistance; MaxAutoExposure = d.MaxAutoExposure;
    }

    /// <summary>A random seed: a different world of the same kind.</summary>
    [RelayCommand]
    private void NewVoxelSeed() => VoxelSeed = Random.Shared.Next(1, 100000);

    private void ApplySkyLook(EnvironmentSettings settings)
    {
        DaySkyColor = FromXna(settings.DaySkyColor);
        DayHorizonColor = FromXna(settings.DayHorizonColor);
        SunsetColor = FromXna(settings.SunsetColor);
        NightSkyColor = FromXna(settings.NightSkyColor);
        NightHorizonColor = FromXna(settings.NightHorizonColor);
        SunBrightness = settings.SunBrightness;
        SunSize = settings.SunSize;
        MoonBrightness = settings.MoonBrightness;
        MoonSize = settings.MoonSize;
        StarBrightness = settings.StarBrightness;
        DayExposure = settings.DayExposure;
        NightExposure = settings.NightExposure;
    }

    private static Microsoft.Xna.Framework.Color ToXna(Color c) => new(c.R, c.G, c.B);
    private static Color FromXna(Microsoft.Xna.Framework.Color c) => Color.FromRgb(c.R, c.G, c.B);

    /// <summary>Restores the default sky colours, sun/moon/stars and exposure; time and clouds are kept.</summary>
    [RelayCommand]
    private void ResetSkyLook()
    {
        var defaults = new EnvironmentSettings();
        Push(s =>
        {
            s.DaySkyColor = defaults.DaySkyColor; s.DayHorizonColor = defaults.DayHorizonColor;
            s.SunsetColor = defaults.SunsetColor; s.NightSkyColor = defaults.NightSkyColor;
            s.NightHorizonColor = defaults.NightHorizonColor; s.SunBrightness = defaults.SunBrightness;
            s.SunSize = defaults.SunSize; s.MoonBrightness = defaults.MoonBrightness; s.MoonSize = defaults.MoonSize;
            s.StarBrightness = defaults.StarBrightness; s.DayExposure = defaults.DayExposure;
            s.NightExposure = defaults.NightExposure;
        });
        _suppress = true;
        try { ApplySkyLook(defaults); }
        finally { _suppress = false; }
    }

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
