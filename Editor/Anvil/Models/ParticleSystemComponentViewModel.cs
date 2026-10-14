using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Engine.Components;
using Engine.Particles;
using XnaColor = Microsoft.Xna.Framework.Color;
using XnaVector3 = Microsoft.Xna.Framework.Vector3;

namespace Anvil.Models;

/// <summary>
/// Inspector for <see cref="ParticleSystemComponent"/>: one field per module setting, grouped like Unity's modules.
/// Lists (bursts, curve and gradient keys, sub-emitters) are rows that send their whole list on each edit.
/// </summary>
public partial class ParticleSystemComponentViewModel : ComponentViewModel
{
    public static ParticleSimulationSpace[] SimulationSpaces { get; } = Enum.GetValues<ParticleSimulationSpace>();
    public static ParticleShapeType[] ShapeTypes { get; } = Enum.GetValues<ParticleShapeType>();
    public static ParticleRenderMode[] RenderModes { get; } = Enum.GetValues<ParticleRenderMode>();
    public static ParticleBlendMode[] BlendModes { get; } = Enum.GetValues<ParticleBlendMode>();
    public static ParticleSortMode[] SortModes { get; } = Enum.GetValues<ParticleSortMode>();
    public static ParticleTexture[] Textures { get; } = Enum.GetValues<ParticleTexture>();
    public static ParticleCollisionType[] CollisionTypes { get; } = Enum.GetValues<ParticleCollisionType>();
    public static ParticleSubEmitterTrigger[] SubEmitterTriggers { get; } = Enum.GetValues<ParticleSubEmitterTrigger>();
    public static ParticleVolumeSource[] VolumeSources { get; } = Enum.GetValues<ParticleVolumeSource>();
    public static ParticleLightMode[] LightModes { get; } = Enum.GetValues<ParticleLightMode>();

    // Main
    [ObservableProperty] private double _duration = 5;
    [ObservableProperty] private bool _looping = true;
    [ObservableProperty] private bool _prewarm;
    [ObservableProperty] private double _startDelay;
    [ObservableProperty] private double _startLifetimeMin = 5;
    [ObservableProperty] private double _startLifetimeMax = 5;
    [ObservableProperty] private double _startSpeedMin = 5;
    [ObservableProperty] private double _startSpeedMax = 5;
    [ObservableProperty] private double _startSizeMin = 1;
    [ObservableProperty] private double _startSizeMax = 1;
    [ObservableProperty] private double _startRotationMin;
    [ObservableProperty] private double _startRotationMax;
    [ObservableProperty] private Color _startColor = Colors.White;
    [ObservableProperty] private Color _startColor2 = Colors.White;
    [ObservableProperty] private double _gravityModifier;
    [ObservableProperty] private ParticleSimulationSpace _simulationSpace = ParticleSimulationSpace.Local;
    [ObservableProperty] private double _simulationSpeed = 1;
    [ObservableProperty] private bool _playOnAwake = true;
    [ObservableProperty] private double _maxParticles = 1000;
    [ObservableProperty] private bool _hideMesh = true;

    // Emission
    [ObservableProperty] private bool _emissionEnabled = true;
    [ObservableProperty] private double _rateOverTime = 10;
    [ObservableProperty] private double _rateOverDistance;
    public ObservableCollection<ParticleBurstRowViewModel> Bursts { get; } = new();

    // Shape
    [ObservableProperty] private bool _shapeEnabled = true;
    [ObservableProperty] private ParticleShapeType _shapeType = ParticleShapeType.Cone;
    [ObservableProperty] private double _angle = 25;
    [ObservableProperty] private double _radius = 1;
    [ObservableProperty] private double _radiusThickness = 1;
    [ObservableProperty] private double _arc = 360;
    [ObservableProperty] private double _boxX = 1;
    [ObservableProperty] private double _boxY = 1;
    [ObservableProperty] private double _boxZ = 1;
    [ObservableProperty] private double _shapePositionX;
    [ObservableProperty] private double _shapePositionY;
    [ObservableProperty] private double _shapePositionZ;
    [ObservableProperty] private double _shapeRotationX;
    [ObservableProperty] private double _shapeRotationY;
    [ObservableProperty] private double _shapeRotationZ;
    [ObservableProperty] private double _randomizeDirection;

    // Velocity over Lifetime
    [ObservableProperty] private bool _velocityEnabled;
    [ObservableProperty] private double _linearX;
    [ObservableProperty] private double _linearY;
    [ObservableProperty] private double _linearZ;
    [ObservableProperty] private ParticleSimulationSpace _velocitySpace = ParticleSimulationSpace.Local;
    [ObservableProperty] private double _orbitalX;
    [ObservableProperty] private double _orbitalY;
    [ObservableProperty] private double _orbitalZ;
    [ObservableProperty] private double _radial;
    public ObservableCollection<ParticleCurveKeyRowViewModel> SpeedKeys { get; } = new();

    // Limit Velocity
    [ObservableProperty] private bool _limitEnabled;
    [ObservableProperty] private double _limitSpeed = 1;
    [ObservableProperty] private double _limitDampen = 0.5;
    [ObservableProperty] private double _drag;

    // Force over Lifetime
    [ObservableProperty] private bool _forceEnabled;
    [ObservableProperty] private double _forceX;
    [ObservableProperty] private double _forceY;
    [ObservableProperty] private double _forceZ;
    [ObservableProperty] private ParticleSimulationSpace _forceSpace = ParticleSimulationSpace.World;

    // External Forces
    [ObservableProperty] private bool _externalEnabled;
    [ObservableProperty] private double _wind = 1;
    [ObservableProperty] private double _windResponse = 1.5;

    // Noise
    [ObservableProperty] private bool _noiseEnabled;
    [ObservableProperty] private double _noiseStrength = 1;
    [ObservableProperty] private double _noiseFrequency = 0.5;
    [ObservableProperty] private double _noiseScrollSpeed = 0.5;

    // Color / Size / Rotation over Lifetime
    [ObservableProperty] private bool _colorEnabled;
    public ObservableCollection<ParticleGradientKeyRowViewModel> ColorKeys { get; } = new();
    [ObservableProperty] private bool _sizeEnabled;
    public ObservableCollection<ParticleCurveKeyRowViewModel> SizeKeys { get; } = new();
    [ObservableProperty] private bool _rotationEnabled;
    [ObservableProperty] private double _angularVelocityMin = 45;
    [ObservableProperty] private double _angularVelocityMax = 45;

    // Collision
    [ObservableProperty] private bool _collisionEnabled;
    [ObservableProperty] private ParticleCollisionType _collisionType = ParticleCollisionType.World;
    [ObservableProperty] private double _planeHeight;
    [ObservableProperty] private double _bounce = 0.5;
    [ObservableProperty] private double _collisionDampen = 0.1;
    [ObservableProperty] private double _lifetimeLoss;
    [ObservableProperty] private double _radiusScale = 1;
    [ObservableProperty] private double _minKillSpeed;

    // Texture Sheet Animation
    [ObservableProperty] private bool _sheetEnabled;
    [ObservableProperty] private double _tilesX = 4;
    [ObservableProperty] private double _tilesY = 4;
    [ObservableProperty] private double _sheetCycles = 1;
    [ObservableProperty] private bool _randomStartFrame;

    // Sub Emitters
    [ObservableProperty] private bool _subEmittersEnabled;
    public ObservableCollection<ParticleSubEmitterRowViewModel> SubEmitters { get; } = new();

    // Renderer
    [ObservableProperty] private ParticleRenderMode _renderMode = ParticleRenderMode.Billboard;
    [ObservableProperty] private ParticleBlendMode _blend = ParticleBlendMode.Alpha;
    [ObservableProperty] private ParticleTexture _texture = ParticleTexture.SoftCircle;
    [ObservableProperty] private string _texturePath = "";
    [ObservableProperty] private ParticleSortMode _sortMode = ParticleSortMode.None;
    [ObservableProperty] private double _brightness = 1;
    [ObservableProperty] private bool _lit;
    [ObservableProperty] private double _softParticles = 0.3;
    [ObservableProperty] private double _cameraFade = 0.5;
    [ObservableProperty] private double _lengthScale = 2;
    [ObservableProperty] private double _speedScale;

    // Volumetric
    [ObservableProperty] private bool _volumetricEnabled;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVoxelSource), nameof(IsParticleSource))]
    private ParticleVolumeSource _volumeSource = ParticleVolumeSource.Particles;
    [ObservableProperty] private bool _volumeDrawParticles;
    [ObservableProperty] private double _volumeDensity = 4;
    [ObservableProperty] private double _volumeNoiseErosion = 0.5;
    [ObservableProperty] private double _volumeNoiseScale = 0.7;
    [ObservableProperty] private double _volumeNoiseSpeed = 0.25;
    [ObservableProperty] private double _volumeSteps = 48;
    [ObservableProperty] private double _volumeRefillTime = 2.5;
    [ObservableProperty] private double _volumeResolution = 32;
    [ObservableProperty] private double _voxelSize = 0.35;
    [ObservableProperty] private double _voxelVolume = 110;
    [ObservableProperty] private double _voxelMaxRadius = 7;
    [ObservableProperty] private double _voxelExpandTime = 1.4;
    [ObservableProperty] private double _voxelFadeTime = 3;
    [ObservableProperty] private double _voxelFlatten = 1.5;

    // Lights
    [ObservableProperty] private bool _lightsEnabled;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSystemLight), nameof(IsPerParticleLight))]
    private ParticleLightMode _lightMode = ParticleLightMode.System;
    [ObservableProperty] private Color _lightColor = Color.FromRgb(255, 150, 70);
    [ObservableProperty] private bool _lightUseParticleColor;
    [ObservableProperty] private double _lightIntensity = 5;
    [ObservableProperty] private double _lightRange = 8;
    [ObservableProperty] private bool _lightAlphaAffectsIntensity = true;
    [ObservableProperty] private double _lightFlicker;
    [ObservableProperty] private double _lightFlickerSpeed = 8;
    [ObservableProperty] private double _lightOffsetX;
    [ObservableProperty] private double _lightOffsetY;
    [ObservableProperty] private double _lightOffsetZ;
    [ObservableProperty] private bool _lightCastShadows;
    [ObservableProperty] private double _lightRatio = 0.1;
    [ObservableProperty] private double _lightMaxLights = 4;
    [ObservableProperty] private bool _lightSizeAffectsRange;

    /// <summary>Offset and shadows show for one System light; Ratio, Max Lights and size for Per Particle.</summary>
    public bool IsSystemLight => LightMode == ParticleLightMode.System;
    public bool IsPerParticleLight => LightMode == ParticleLightMode.PerParticle;

    /// <summary>The voxel (Counter-Strike 2 smoke) settings show only for the Voxels source.</summary>
    public bool IsVoxelSource => VolumeSource == ParticleVolumeSource.Voxels;
    public bool IsParticleSource => VolumeSource == ParticleVolumeSource.Particles;

    public IRelayCommand AddBurstCommand { get; }
    public IRelayCommand AddColorKeyCommand { get; }
    public IRelayCommand AddSizeKeyCommand { get; }
    public IRelayCommand AddSpeedKeyCommand { get; }
    public IRelayCommand AddSubEmitterCommand { get; }

    // Set while Apply copies the engine's values, whose setters would otherwise push them straight back.
    private bool _applying;

    public ParticleSystemComponentViewModel(SceneObjectViewModel owner) : base(owner, ParticleSystemComponent.TypeId)
    {
        AddBurstCommand = new RelayCommand(() =>
        {
            Bursts.Add(NewBurstRow(new ParticleBurst(Bursts.Count > 0 ? Bursts.Max(b => (float)b.Time) + 0.5f : 0, 30)));
            PushBursts();
        });
        AddColorKeyCommand = new RelayCommand(() =>
        {
            if (ColorKeys.Count >= ParticleGradient.MaxKeys) return;
            ColorKeys.Add(NewColorRow(new ParticleGradientKey(0.5f, XnaColor.White)));
            PushColorKeys();
        });
        AddSizeKeyCommand = new RelayCommand(() =>
        {
            if (SizeKeys.Count >= ParticleCurve.MaxKeys) return;
            SizeKeys.Add(NewCurveRow(new ParticleCurveKey(0.5f, 1), PushSizeKeys));
            PushSizeKeys();
        });
        AddSpeedKeyCommand = new RelayCommand(() =>
        {
            if (SpeedKeys.Count >= ParticleCurve.MaxKeys) return;
            SpeedKeys.Add(NewCurveRow(new ParticleCurveKey(0.5f, 1), PushSpeedKeys));
            PushSpeedKeys();
        });
        AddSubEmitterCommand = new RelayCommand(() =>
        {
            SubEmitters.Add(NewSubEmitterRow(new ParticleSubEmitter()));
            PushSubEmitters();
        });
    }

    // ---------------------------------------------------------------------------------------------
    // Pushing edits
    // ---------------------------------------------------------------------------------------------

    private void Set(Action<ParticleSystemComponent> mutate)
    {
        if (_applying) return;
        Push(c => mutate((ParticleSystemComponent)c));
    }

    private void SetNumber(double value, Action<ParticleSystemComponent, float> apply)
    {
        if (!double.IsFinite(value)) return;
        float number = (float)value;
        Set(c => apply(c, number));
    }

    private void SetEnum<T>(T value, Action<ParticleSystemComponent, T> apply) where T : struct, Enum
    {
        if (!Enum.IsDefined(value)) return;
        Set(c => apply(c, value));
    }

    partial void OnDurationChanged(double value) => SetNumber(Math.Max(value, 0.05), (c, v) => c.Main.Duration = v);
    partial void OnLoopingChanged(bool value) => Set(c => c.Main.Looping = value);
    partial void OnPrewarmChanged(bool value) => Set(c => c.Main.Prewarm = value);
    partial void OnStartDelayChanged(double value) => SetNumber(Math.Max(value, 0), (c, v) => c.Main.StartDelay = v);
    partial void OnStartLifetimeMinChanged(double value) => SetNumber(value, (c, v) => c.Main.StartLifetime = c.Main.StartLifetime with { Min = v });
    partial void OnStartLifetimeMaxChanged(double value) => SetNumber(value, (c, v) => c.Main.StartLifetime = c.Main.StartLifetime with { Max = v });
    partial void OnStartSpeedMinChanged(double value) => SetNumber(value, (c, v) => c.Main.StartSpeed = c.Main.StartSpeed with { Min = v });
    partial void OnStartSpeedMaxChanged(double value) => SetNumber(value, (c, v) => c.Main.StartSpeed = c.Main.StartSpeed with { Max = v });
    partial void OnStartSizeMinChanged(double value) => SetNumber(value, (c, v) => c.Main.StartSize = c.Main.StartSize with { Min = v });
    partial void OnStartSizeMaxChanged(double value) => SetNumber(value, (c, v) => c.Main.StartSize = c.Main.StartSize with { Max = v });
    partial void OnStartRotationMinChanged(double value) => SetNumber(value, (c, v) => c.Main.StartRotation = c.Main.StartRotation with { Min = v });
    partial void OnStartRotationMaxChanged(double value) => SetNumber(value, (c, v) => c.Main.StartRotation = c.Main.StartRotation with { Max = v });
    partial void OnStartColorChanged(Color value)
    {
        XnaColor color = ToXna(value);
        Set(c => c.Main.StartColor = color);
    }
    partial void OnStartColor2Changed(Color value)
    {
        XnaColor color = ToXna(value);
        Set(c => c.Main.StartColor2 = color);
    }
    partial void OnGravityModifierChanged(double value) => SetNumber(value, (c, v) => c.Main.GravityModifier = v);
    partial void OnSimulationSpaceChanged(ParticleSimulationSpace value) => SetEnum(value, (c, v) => c.Main.SimulationSpace = v);
    partial void OnSimulationSpeedChanged(double value) => SetNumber(Math.Max(value, 0), (c, v) => c.Main.SimulationSpeed = v);
    partial void OnPlayOnAwakeChanged(bool value) => Set(c => c.Main.PlayOnAwake = value);
    partial void OnMaxParticlesChanged(double value)
    {
        if (!double.IsFinite(value)) return;
        int max = (int)Math.Clamp(Math.Round(value), 0, ParticleMainModule.MaxParticlesLimit);
        Set(c => c.Main.MaxParticles = max);
    }
    partial void OnHideMeshChanged(bool value) => Set(c => c.HideMesh = value);

    partial void OnEmissionEnabledChanged(bool value) => Set(c => c.Emission.Enabled = value);
    partial void OnRateOverTimeChanged(double value) => SetNumber(Math.Max(value, 0), (c, v) => c.Emission.RateOverTime = v);
    partial void OnRateOverDistanceChanged(double value) => SetNumber(Math.Max(value, 0), (c, v) => c.Emission.RateOverDistance = v);

    partial void OnShapeEnabledChanged(bool value) => Set(c => c.Shape.Enabled = value);
    partial void OnShapeTypeChanged(ParticleShapeType value) => SetEnum(value, (c, v) => c.Shape.Shape = v);
    partial void OnAngleChanged(double value) => SetNumber(Math.Clamp(value, 0, 90), (c, v) => c.Shape.Angle = v);
    partial void OnRadiusChanged(double value) => SetNumber(Math.Max(value, 0), (c, v) => c.Shape.Radius = v);
    partial void OnRadiusThicknessChanged(double value) => SetNumber(Math.Clamp(value, 0, 1), (c, v) => c.Shape.RadiusThickness = v);
    partial void OnArcChanged(double value) => SetNumber(Math.Clamp(value, 0, 360), (c, v) => c.Shape.Arc = v);
    partial void OnBoxXChanged(double value) => SetNumber(Math.Max(value, 0), (c, v) => c.Shape.BoxSize = c.Shape.BoxSize with { X = v });
    partial void OnBoxYChanged(double value) => SetNumber(Math.Max(value, 0), (c, v) => c.Shape.BoxSize = c.Shape.BoxSize with { Y = v });
    partial void OnBoxZChanged(double value) => SetNumber(Math.Max(value, 0), (c, v) => c.Shape.BoxSize = c.Shape.BoxSize with { Z = v });
    partial void OnShapePositionXChanged(double value) => SetNumber(value, (c, v) => c.Shape.Position = c.Shape.Position with { X = v });
    partial void OnShapePositionYChanged(double value) => SetNumber(value, (c, v) => c.Shape.Position = c.Shape.Position with { Y = v });
    partial void OnShapePositionZChanged(double value) => SetNumber(value, (c, v) => c.Shape.Position = c.Shape.Position with { Z = v });
    partial void OnShapeRotationXChanged(double value) => SetNumber(value, (c, v) => c.Shape.Rotation = c.Shape.Rotation with { X = v });
    partial void OnShapeRotationYChanged(double value) => SetNumber(value, (c, v) => c.Shape.Rotation = c.Shape.Rotation with { Y = v });
    partial void OnShapeRotationZChanged(double value) => SetNumber(value, (c, v) => c.Shape.Rotation = c.Shape.Rotation with { Z = v });
    partial void OnRandomizeDirectionChanged(double value) => SetNumber(Math.Clamp(value, 0, 1), (c, v) => c.Shape.RandomizeDirection = v);

    partial void OnVelocityEnabledChanged(bool value) => Set(c => c.VelocityOverLifetime.Enabled = value);
    partial void OnLinearXChanged(double value) => SetNumber(value, (c, v) => c.VelocityOverLifetime.Linear = c.VelocityOverLifetime.Linear with { X = v });
    partial void OnLinearYChanged(double value) => SetNumber(value, (c, v) => c.VelocityOverLifetime.Linear = c.VelocityOverLifetime.Linear with { Y = v });
    partial void OnLinearZChanged(double value) => SetNumber(value, (c, v) => c.VelocityOverLifetime.Linear = c.VelocityOverLifetime.Linear with { Z = v });
    partial void OnVelocitySpaceChanged(ParticleSimulationSpace value) => SetEnum(value, (c, v) => c.VelocityOverLifetime.Space = v);
    partial void OnOrbitalXChanged(double value) => SetNumber(value, (c, v) => c.VelocityOverLifetime.Orbital = c.VelocityOverLifetime.Orbital with { X = v });
    partial void OnOrbitalYChanged(double value) => SetNumber(value, (c, v) => c.VelocityOverLifetime.Orbital = c.VelocityOverLifetime.Orbital with { Y = v });
    partial void OnOrbitalZChanged(double value) => SetNumber(value, (c, v) => c.VelocityOverLifetime.Orbital = c.VelocityOverLifetime.Orbital with { Z = v });
    partial void OnRadialChanged(double value) => SetNumber(value, (c, v) => c.VelocityOverLifetime.Radial = v);

    partial void OnLimitEnabledChanged(bool value) => Set(c => c.LimitVelocity.Enabled = value);
    partial void OnLimitSpeedChanged(double value) => SetNumber(Math.Max(value, 0), (c, v) => c.LimitVelocity.Speed = v);
    partial void OnLimitDampenChanged(double value) => SetNumber(Math.Clamp(value, 0, 1), (c, v) => c.LimitVelocity.Dampen = v);
    partial void OnDragChanged(double value) => SetNumber(Math.Max(value, 0), (c, v) => c.LimitVelocity.Drag = v);

    partial void OnForceEnabledChanged(bool value) => Set(c => c.ForceOverLifetime.Enabled = value);
    partial void OnForceXChanged(double value) => SetNumber(value, (c, v) => c.ForceOverLifetime.Force = c.ForceOverLifetime.Force with { X = v });
    partial void OnForceYChanged(double value) => SetNumber(value, (c, v) => c.ForceOverLifetime.Force = c.ForceOverLifetime.Force with { Y = v });
    partial void OnForceZChanged(double value) => SetNumber(value, (c, v) => c.ForceOverLifetime.Force = c.ForceOverLifetime.Force with { Z = v });
    partial void OnForceSpaceChanged(ParticleSimulationSpace value) => SetEnum(value, (c, v) => c.ForceOverLifetime.Space = v);

    partial void OnExternalEnabledChanged(bool value) => Set(c => c.ExternalForces.Enabled = value);
    partial void OnWindChanged(double value) => SetNumber(value, (c, v) => c.ExternalForces.Wind = v);
    partial void OnWindResponseChanged(double value) => SetNumber(Math.Max(value, 0), (c, v) => c.ExternalForces.Response = v);

    partial void OnNoiseEnabledChanged(bool value) => Set(c => c.Noise.Enabled = value);
    partial void OnNoiseStrengthChanged(double value) => SetNumber(value, (c, v) => c.Noise.Strength = v);
    partial void OnNoiseFrequencyChanged(double value) => SetNumber(Math.Max(value, 0), (c, v) => c.Noise.Frequency = v);
    partial void OnNoiseScrollSpeedChanged(double value) => SetNumber(value, (c, v) => c.Noise.ScrollSpeed = v);

    partial void OnColorEnabledChanged(bool value) => Set(c => c.ColorOverLifetime.Enabled = value);
    partial void OnSizeEnabledChanged(bool value) => Set(c => c.SizeOverLifetime.Enabled = value);
    partial void OnRotationEnabledChanged(bool value) => Set(c => c.RotationOverLifetime.Enabled = value);
    partial void OnAngularVelocityMinChanged(double value) => SetNumber(value, (c, v) => c.RotationOverLifetime.AngularVelocity = c.RotationOverLifetime.AngularVelocity with { Min = v });
    partial void OnAngularVelocityMaxChanged(double value) => SetNumber(value, (c, v) => c.RotationOverLifetime.AngularVelocity = c.RotationOverLifetime.AngularVelocity with { Max = v });

    partial void OnCollisionEnabledChanged(bool value) => Set(c => c.Collision.Enabled = value);
    partial void OnCollisionTypeChanged(ParticleCollisionType value) => SetEnum(value, (c, v) => c.Collision.Type = v);
    partial void OnPlaneHeightChanged(double value) => SetNumber(value, (c, v) => c.Collision.PlaneHeight = v);
    partial void OnBounceChanged(double value) => SetNumber(Math.Clamp(value, 0, 2), (c, v) => c.Collision.Bounce = v);
    partial void OnCollisionDampenChanged(double value) => SetNumber(Math.Clamp(value, 0, 1), (c, v) => c.Collision.Dampen = v);
    partial void OnLifetimeLossChanged(double value) => SetNumber(Math.Clamp(value, 0, 1), (c, v) => c.Collision.LifetimeLoss = v);
    partial void OnRadiusScaleChanged(double value) => SetNumber(Math.Max(value, 0), (c, v) => c.Collision.RadiusScale = v);
    partial void OnMinKillSpeedChanged(double value) => SetNumber(Math.Max(value, 0), (c, v) => c.Collision.MinKillSpeed = v);

    partial void OnSheetEnabledChanged(bool value) => Set(c => c.TextureSheetAnimation.Enabled = value);
    partial void OnTilesXChanged(double value)
    {
        if (!double.IsFinite(value)) return;
        int tiles = (int)Math.Clamp(Math.Round(value), 1, 64);
        Set(c => c.TextureSheetAnimation.TilesX = tiles);
    }
    partial void OnTilesYChanged(double value)
    {
        if (!double.IsFinite(value)) return;
        int tiles = (int)Math.Clamp(Math.Round(value), 1, 64);
        Set(c => c.TextureSheetAnimation.TilesY = tiles);
    }
    partial void OnSheetCyclesChanged(double value) => SetNumber(Math.Max(value, 0), (c, v) => c.TextureSheetAnimation.Cycles = v);
    partial void OnRandomStartFrameChanged(bool value) => Set(c => c.TextureSheetAnimation.RandomStartFrame = value);

    partial void OnSubEmittersEnabledChanged(bool value) => Set(c => c.SubEmitters.Enabled = value);

    partial void OnRenderModeChanged(ParticleRenderMode value) => SetEnum(value, (c, v) => c.Renderer.RenderMode = v);
    partial void OnBlendChanged(ParticleBlendMode value) => SetEnum(value, (c, v) => c.Renderer.Blend = v);
    partial void OnTextureChanged(ParticleTexture value) => SetEnum(value, (c, v) => c.Renderer.Texture = v);
    partial void OnTexturePathChanged(string value)
    {
        string path = value ?? "";
        Set(c => c.Renderer.TexturePath = path);
    }
    partial void OnSortModeChanged(ParticleSortMode value) => SetEnum(value, (c, v) => c.Renderer.SortMode = v);
    partial void OnBrightnessChanged(double value) => SetNumber(Math.Max(value, 0), (c, v) => c.Renderer.Brightness = v);
    partial void OnLitChanged(bool value) => Set(c => c.Renderer.Lit = value);
    partial void OnSoftParticlesChanged(double value) => SetNumber(Math.Max(value, 0), (c, v) => c.Renderer.SoftParticles = v);
    partial void OnCameraFadeChanged(double value) => SetNumber(Math.Max(value, 0), (c, v) => c.Renderer.CameraFade = v);
    partial void OnLengthScaleChanged(double value) => SetNumber(Math.Max(value, 0), (c, v) => c.Renderer.LengthScale = v);
    partial void OnSpeedScaleChanged(double value) => SetNumber(Math.Max(value, 0), (c, v) => c.Renderer.SpeedScale = v);

    partial void OnVolumetricEnabledChanged(bool value) => Set(c => c.Volumetric.Enabled = value);
    partial void OnVolumeSourceChanged(ParticleVolumeSource value) => SetEnum(value, (c, v) => c.Volumetric.Source = v);
    partial void OnVolumeDrawParticlesChanged(bool value) => Set(c => c.Volumetric.DrawParticles = value);
    partial void OnVolumeDensityChanged(double value) => SetNumber(Math.Max(value, 0), (c, v) => c.Volumetric.Density = v);
    partial void OnVolumeNoiseErosionChanged(double value) => SetNumber(Math.Clamp(value, 0, 1), (c, v) => c.Volumetric.NoiseErosion = v);
    partial void OnVolumeNoiseScaleChanged(double value) => SetNumber(Math.Max(value, 0.001), (c, v) => c.Volumetric.NoiseScale = v);
    partial void OnVolumeNoiseSpeedChanged(double value) => SetNumber(value, (c, v) => c.Volumetric.NoiseSpeed = v);
    partial void OnVolumeStepsChanged(double value)
    {
        if (!double.IsFinite(value)) return;
        int steps = (int)Math.Clamp(Math.Round(value), 8, 128);
        Set(c => c.Volumetric.Steps = steps);
    }
    partial void OnVolumeRefillTimeChanged(double value) => SetNumber(Math.Max(value, 0.01), (c, v) => c.Volumetric.RefillTime = v);
    partial void OnVolumeResolutionChanged(double value)
    {
        if (!double.IsFinite(value)) return;
        int resolution = (int)Math.Clamp(Math.Round(value), 4, ParticleVolumetricModule.MaxResolution - 2);
        Set(c => c.Volumetric.Resolution = resolution);
    }
    partial void OnVoxelSizeChanged(double value) => SetNumber(Math.Clamp(value, 0.05, 4), (c, v) => c.Volumetric.VoxelSize = v);
    partial void OnVoxelVolumeChanged(double value) => SetNumber(Math.Max(value, 0), (c, v) => c.Volumetric.Volume = v);
    partial void OnVoxelMaxRadiusChanged(double value) => SetNumber(Math.Clamp(value, 0.25, 100), (c, v) => c.Volumetric.MaxRadius = v);
    partial void OnVoxelExpandTimeChanged(double value) => SetNumber(Math.Max(value, 0.01), (c, v) => c.Volumetric.ExpandTime = v);
    partial void OnVoxelFadeTimeChanged(double value) => SetNumber(Math.Max(value, 0.01), (c, v) => c.Volumetric.FadeTime = v);
    partial void OnVoxelFlattenChanged(double value) => SetNumber(Math.Clamp(value, 0.1, 10), (c, v) => c.Volumetric.Flatten = v);

    partial void OnLightsEnabledChanged(bool value) => Set(c => c.Lights.Enabled = value);
    partial void OnLightModeChanged(ParticleLightMode value) => SetEnum(value, (c, v) => c.Lights.Mode = v);
    partial void OnLightColorChanged(Color value)
    {
        XnaColor color = ToXna(value);
        Set(c => c.Lights.Color = color);
    }
    partial void OnLightUseParticleColorChanged(bool value) => Set(c => c.Lights.UseParticleColor = value);
    partial void OnLightIntensityChanged(double value) => SetNumber(Math.Max(value, 0), (c, v) => c.Lights.Intensity = v);
    partial void OnLightRangeChanged(double value) => SetNumber(Math.Clamp(value, 0.1, 1000), (c, v) => c.Lights.Range = v);
    partial void OnLightAlphaAffectsIntensityChanged(bool value) => Set(c => c.Lights.AlphaAffectsIntensity = value);
    partial void OnLightFlickerChanged(double value) => SetNumber(Math.Clamp(value, 0, 1), (c, v) => c.Lights.Flicker = v);
    partial void OnLightFlickerSpeedChanged(double value) => SetNumber(Math.Clamp(value, 0, 100), (c, v) => c.Lights.FlickerSpeed = v);
    partial void OnLightOffsetXChanged(double value) => SetNumber(value, (c, v) => c.Lights.Offset = c.Lights.Offset with { X = v });
    partial void OnLightOffsetYChanged(double value) => SetNumber(value, (c, v) => c.Lights.Offset = c.Lights.Offset with { Y = v });
    partial void OnLightOffsetZChanged(double value) => SetNumber(value, (c, v) => c.Lights.Offset = c.Lights.Offset with { Z = v });
    partial void OnLightCastShadowsChanged(bool value) => Set(c => c.Lights.CastShadows = value);
    partial void OnLightRatioChanged(double value) => SetNumber(Math.Clamp(value, 0, 1), (c, v) => c.Lights.Ratio = v);
    partial void OnLightMaxLightsChanged(double value)
    {
        if (!double.IsFinite(value)) return;
        int max = (int)Math.Clamp(Math.Round(value), 0, ParticleLightsModule.MaxLightsLimit);
        Set(c => c.Lights.MaxLights = max);
    }
    partial void OnLightSizeAffectsRangeChanged(bool value) => Set(c => c.Lights.SizeAffectsRange = value);

    // ---------------------------------------------------------------------------------------------
    // Rows
    // ---------------------------------------------------------------------------------------------

    private ParticleBurstRowViewModel NewBurstRow(ParticleBurst burst) =>
        new(burst, PushBursts, row => { if (Bursts.Remove((ParticleBurstRowViewModel)row)) PushBursts(); });

    private ParticleGradientKeyRowViewModel NewColorRow(ParticleGradientKey key) =>
        new(key, PushColorKeys, row => { if (ColorKeys.Remove((ParticleGradientKeyRowViewModel)row)) PushColorKeys(); });

    private ParticleCurveKeyRowViewModel NewCurveRow(ParticleCurveKey key, Action push) =>
        new(key, push, row =>
        {
            if (SizeKeys.Remove((ParticleCurveKeyRowViewModel)row)) PushSizeKeys();
            else if (SpeedKeys.Remove((ParticleCurveKeyRowViewModel)row)) PushSpeedKeys();
        });

    private ParticleSubEmitterRowViewModel NewSubEmitterRow(ParticleSubEmitter emitter) =>
        new(emitter, PushSubEmitters, row => { if (SubEmitters.Remove((ParticleSubEmitterRowViewModel)row)) PushSubEmitters(); });

    internal void PushBursts()
    {
        var bursts = Bursts.Select(b => b.ToModel()).ToList();
        Set(c => c.Emission.Bursts = bursts);
    }

    internal void PushColorKeys()
    {
        var keys = ColorKeys.Select(k => k.ToModel()).ToList();
        Set(c => c.ColorOverLifetime.Color = new ParticleGradient { Keys = keys });
    }

    internal void PushSizeKeys()
    {
        var keys = SizeKeys.Select(k => k.ToModel()).ToList();
        Set(c => c.SizeOverLifetime.Size = new ParticleCurve { Keys = keys });
    }

    internal void PushSpeedKeys()
    {
        var keys = SpeedKeys.Select(k => k.ToModel()).ToList();
        Set(c => c.VelocityOverLifetime.SpeedModifier = new ParticleCurve { Keys = keys });
    }

    internal void PushSubEmitters()
    {
        var emitters = SubEmitters.Select(s => s.ToModel()).ToList();
        Set(c => c.SubEmitters.Emitters = emitters);
    }

    // ---------------------------------------------------------------------------------------------
    // Engine -> inspector
    // ---------------------------------------------------------------------------------------------

    public override void Apply(GameComponent component, bool freezeFields)
    {
        base.Apply(component, freezeFields);
        if (freezeFields) return;
        var p = (ParticleSystemComponent)component;
        _applying = true;
        try
        {
            ParticleMainModule main = p.Main;
            Number(Duration, main.Duration, v => Duration = v);
            Looping = main.Looping;
            Prewarm = main.Prewarm;
            Number(StartDelay, main.StartDelay.Min, v => StartDelay = v);
            Number(StartLifetimeMin, main.StartLifetime.Min, v => StartLifetimeMin = v);
            Number(StartLifetimeMax, main.StartLifetime.Max, v => StartLifetimeMax = v);
            Number(StartSpeedMin, main.StartSpeed.Min, v => StartSpeedMin = v);
            Number(StartSpeedMax, main.StartSpeed.Max, v => StartSpeedMax = v);
            Number(StartSizeMin, main.StartSize.Min, v => StartSizeMin = v);
            Number(StartSizeMax, main.StartSize.Max, v => StartSizeMax = v);
            Number(StartRotationMin, main.StartRotation.Min, v => StartRotationMin = v);
            Number(StartRotationMax, main.StartRotation.Max, v => StartRotationMax = v);
            StartColor = FromXna(main.StartColor);
            StartColor2 = FromXna(main.StartColor2);
            Number(GravityModifier, main.GravityModifier, v => GravityModifier = v);
            SimulationSpace = main.SimulationSpace;
            Number(SimulationSpeed, main.SimulationSpeed, v => SimulationSpeed = v);
            PlayOnAwake = main.PlayOnAwake;
            Number(MaxParticles, main.MaxParticles, v => MaxParticles = v);
            HideMesh = p.HideMesh;

            ParticleEmissionModule emission = p.Emission;
            EmissionEnabled = emission.Enabled;
            Number(RateOverTime, emission.RateOverTime, v => RateOverTime = v);
            Number(RateOverDistance, emission.RateOverDistance, v => RateOverDistance = v);
            ApplyRows(Bursts, emission.Bursts, NewBurstRow, (row, model) => row.Apply(model));

            ParticleShapeModule shape = p.Shape;
            ShapeEnabled = shape.Enabled;
            ShapeType = shape.Shape;
            Number(Angle, shape.Angle, v => Angle = v);
            Number(Radius, shape.Radius, v => Radius = v);
            Number(RadiusThickness, shape.RadiusThickness, v => RadiusThickness = v);
            Number(Arc, shape.Arc, v => Arc = v);
            Number(BoxX, shape.BoxSize.X, v => BoxX = v);
            Number(BoxY, shape.BoxSize.Y, v => BoxY = v);
            Number(BoxZ, shape.BoxSize.Z, v => BoxZ = v);
            Number(ShapePositionX, shape.Position.X, v => ShapePositionX = v);
            Number(ShapePositionY, shape.Position.Y, v => ShapePositionY = v);
            Number(ShapePositionZ, shape.Position.Z, v => ShapePositionZ = v);
            Number(ShapeRotationX, shape.Rotation.X, v => ShapeRotationX = v);
            Number(ShapeRotationY, shape.Rotation.Y, v => ShapeRotationY = v);
            Number(ShapeRotationZ, shape.Rotation.Z, v => ShapeRotationZ = v);
            Number(RandomizeDirection, shape.RandomizeDirection, v => RandomizeDirection = v);

            ParticleVelocityModule velocity = p.VelocityOverLifetime;
            VelocityEnabled = velocity.Enabled;
            Number(LinearX, velocity.Linear.X, v => LinearX = v);
            Number(LinearY, velocity.Linear.Y, v => LinearY = v);
            Number(LinearZ, velocity.Linear.Z, v => LinearZ = v);
            VelocitySpace = velocity.Space;
            Number(OrbitalX, velocity.Orbital.X, v => OrbitalX = v);
            Number(OrbitalY, velocity.Orbital.Y, v => OrbitalY = v);
            Number(OrbitalZ, velocity.Orbital.Z, v => OrbitalZ = v);
            Number(Radial, velocity.Radial, v => Radial = v);
            ApplyRows(SpeedKeys, velocity.SpeedModifier?.Keys, key => NewCurveRow(key, PushSpeedKeys), (row, model) => row.Apply(model));

            ParticleLimitVelocityModule limit = p.LimitVelocity;
            LimitEnabled = limit.Enabled;
            Number(LimitSpeed, limit.Speed, v => LimitSpeed = v);
            Number(LimitDampen, limit.Dampen, v => LimitDampen = v);
            Number(Drag, limit.Drag, v => Drag = v);

            ParticleForceModule force = p.ForceOverLifetime;
            ForceEnabled = force.Enabled;
            Number(ForceX, force.Force.X, v => ForceX = v);
            Number(ForceY, force.Force.Y, v => ForceY = v);
            Number(ForceZ, force.Force.Z, v => ForceZ = v);
            ForceSpace = force.Space;

            ExternalEnabled = p.ExternalForces.Enabled;
            Number(Wind, p.ExternalForces.Wind, v => Wind = v);
            Number(WindResponse, p.ExternalForces.Response, v => WindResponse = v);

            NoiseEnabled = p.Noise.Enabled;
            Number(NoiseStrength, p.Noise.Strength, v => NoiseStrength = v);
            Number(NoiseFrequency, p.Noise.Frequency, v => NoiseFrequency = v);
            Number(NoiseScrollSpeed, p.Noise.ScrollSpeed, v => NoiseScrollSpeed = v);

            ColorEnabled = p.ColorOverLifetime.Enabled;
            ApplyRows(ColorKeys, p.ColorOverLifetime.Color?.Keys, NewColorRow, (row, model) => row.Apply(model));
            SizeEnabled = p.SizeOverLifetime.Enabled;
            ApplyRows(SizeKeys, p.SizeOverLifetime.Size?.Keys, key => NewCurveRow(key, PushSizeKeys), (row, model) => row.Apply(model));
            RotationEnabled = p.RotationOverLifetime.Enabled;
            Number(AngularVelocityMin, p.RotationOverLifetime.AngularVelocity.Min, v => AngularVelocityMin = v);
            Number(AngularVelocityMax, p.RotationOverLifetime.AngularVelocity.Max, v => AngularVelocityMax = v);

            ParticleCollisionModule collision = p.Collision;
            CollisionEnabled = collision.Enabled;
            CollisionType = collision.Type;
            Number(PlaneHeight, collision.PlaneHeight, v => PlaneHeight = v);
            Number(Bounce, collision.Bounce, v => Bounce = v);
            Number(CollisionDampen, collision.Dampen, v => CollisionDampen = v);
            Number(LifetimeLoss, collision.LifetimeLoss, v => LifetimeLoss = v);
            Number(RadiusScale, collision.RadiusScale, v => RadiusScale = v);
            Number(MinKillSpeed, collision.MinKillSpeed, v => MinKillSpeed = v);

            ParticleTextureSheetModule sheet = p.TextureSheetAnimation;
            SheetEnabled = sheet.Enabled;
            Number(TilesX, sheet.TilesX, v => TilesX = v);
            Number(TilesY, sheet.TilesY, v => TilesY = v);
            Number(SheetCycles, sheet.Cycles, v => SheetCycles = v);
            RandomStartFrame = sheet.RandomStartFrame;

            SubEmittersEnabled = p.SubEmitters.Enabled;
            ApplyRows(SubEmitters, p.SubEmitters.Emitters, NewSubEmitterRow, (row, model) => row.Apply(model));

            ParticleRendererModule renderer = p.Renderer;
            RenderMode = renderer.RenderMode;
            Blend = renderer.Blend;
            Texture = renderer.Texture;
            if (TexturePath != (renderer.TexturePath ?? "")) TexturePath = renderer.TexturePath ?? "";
            SortMode = renderer.SortMode;
            Number(Brightness, renderer.Brightness, v => Brightness = v);
            Lit = renderer.Lit;
            Number(SoftParticles, renderer.SoftParticles, v => SoftParticles = v);
            Number(CameraFade, renderer.CameraFade, v => CameraFade = v);
            Number(LengthScale, renderer.LengthScale, v => LengthScale = v);
            Number(SpeedScale, renderer.SpeedScale, v => SpeedScale = v);

            ParticleVolumetricModule volume = p.Volumetric;
            VolumetricEnabled = volume.Enabled;
            VolumeSource = volume.Source;
            VolumeDrawParticles = volume.DrawParticles;
            Number(VolumeDensity, volume.Density, v => VolumeDensity = v);
            Number(VolumeNoiseErosion, volume.NoiseErosion, v => VolumeNoiseErosion = v);
            Number(VolumeNoiseScale, volume.NoiseScale, v => VolumeNoiseScale = v);
            Number(VolumeNoiseSpeed, volume.NoiseSpeed, v => VolumeNoiseSpeed = v);
            Number(VolumeSteps, volume.Steps, v => VolumeSteps = v);
            Number(VolumeRefillTime, volume.RefillTime, v => VolumeRefillTime = v);
            Number(VolumeResolution, volume.Resolution, v => VolumeResolution = v);
            Number(VoxelSize, volume.VoxelSize, v => VoxelSize = v);
            Number(VoxelVolume, volume.Volume, v => VoxelVolume = v);
            Number(VoxelMaxRadius, volume.MaxRadius, v => VoxelMaxRadius = v);
            Number(VoxelExpandTime, volume.ExpandTime, v => VoxelExpandTime = v);
            Number(VoxelFadeTime, volume.FadeTime, v => VoxelFadeTime = v);
            Number(VoxelFlatten, volume.Flatten, v => VoxelFlatten = v);

            ParticleLightsModule lights = p.Lights;
            LightsEnabled = lights.Enabled;
            LightMode = lights.Mode;
            LightColor = FromXna(lights.Color);
            LightUseParticleColor = lights.UseParticleColor;
            Number(LightIntensity, lights.Intensity, v => LightIntensity = v);
            Number(LightRange, lights.Range, v => LightRange = v);
            LightAlphaAffectsIntensity = lights.AlphaAffectsIntensity;
            Number(LightFlicker, lights.Flicker, v => LightFlicker = v);
            Number(LightFlickerSpeed, lights.FlickerSpeed, v => LightFlickerSpeed = v);
            Number(LightOffsetX, lights.Offset.X, v => LightOffsetX = v);
            Number(LightOffsetY, lights.Offset.Y, v => LightOffsetY = v);
            Number(LightOffsetZ, lights.Offset.Z, v => LightOffsetZ = v);
            LightCastShadows = lights.CastShadows;
            Number(LightRatio, lights.Ratio, v => LightRatio = v);
            Number(LightMaxLights, lights.MaxLights, v => LightMaxLights = v);
            LightSizeAffectsRange = lights.SizeAffectsRange;
        }
        finally
        {
            _applying = false;
        }
    }

    // Only real changes: rewriting a value the user is typing would move the caret.
    private static void Number(double current, double engine, Action<double> set)
    {
        if (double.IsFinite(engine) && Math.Abs(current - engine) > 0.0001) set(engine);
    }

    /// <summary>Same row count: update in place so a focused field keeps its control; otherwise rebuild.</summary>
    private static void ApplyRows<TRow, TModel>(ObservableCollection<TRow> rows, IList<TModel>? models,
        Func<TModel, TRow> create, Action<TRow, TModel> apply) where TModel : class, new()
    {
        models ??= Array.Empty<TModel>();
        if (rows.Count != models.Count)
        {
            rows.Clear();
            foreach (TModel model in models) rows.Add(create(model ?? new TModel()));
            return;
        }
        for (int i = 0; i < models.Count; i++) apply(rows[i], models[i] ?? new TModel());
    }

    internal static XnaColor ToXna(Color c) => new(c.R, c.G, c.B, c.A);
    internal static Color FromXna(XnaColor c) => Color.FromArgb(c.A, c.R, c.G, c.B);
}

/// <summary>A row of one of the particle inspector's lists; any edit sends the parent's whole list.</summary>
public abstract class ParticleRowViewModel : ObservableObject
{
    private readonly Action _changed;
    // Set while the row shows the engine's values (its setters would push them back).
    protected bool Applying;
    public IRelayCommand RemoveCommand { get; }

    protected ParticleRowViewModel(Action changed, Action<ParticleRowViewModel> remove)
    {
        _changed = changed;
        RemoveCommand = new RelayCommand(() => remove(this));
    }

    protected void Changed()
    {
        if (!Applying) _changed();
    }

    protected static bool Differs(double current, double engine) => double.IsFinite(engine) && Math.Abs(current - engine) > 0.0001;
}

public sealed partial class ParticleBurstRowViewModel : ParticleRowViewModel
{
    [ObservableProperty] private double _time;
    [ObservableProperty] private double _countMin;
    [ObservableProperty] private double _countMax;
    [ObservableProperty] private double _cycles;
    [ObservableProperty] private double _interval;
    [ObservableProperty] private double _probability;

    public ParticleBurstRowViewModel(ParticleBurst burst, Action changed, Action<ParticleRowViewModel> remove)
        : base(changed, remove) => Apply(burst);

    internal void Apply(ParticleBurst burst)
    {
        Applying = true;
        if (Differs(Time, burst.Time)) Time = burst.Time;
        if (Differs(CountMin, burst.Count.Min)) CountMin = burst.Count.Min;
        if (Differs(CountMax, burst.Count.Max)) CountMax = burst.Count.Max;
        if (Differs(Cycles, burst.Cycles)) Cycles = burst.Cycles;
        if (Differs(Interval, burst.Interval)) Interval = burst.Interval;
        if (Differs(Probability, burst.Probability)) Probability = burst.Probability;
        Applying = false;
    }

    internal ParticleBurst ToModel() => new()
    {
        Time = (float)Math.Max(Finite(Time), 0),
        Count = new ParticleRange((float)Math.Max(Finite(CountMin), 0), (float)Math.Max(Finite(CountMax), 0)),
        Cycles = (int)Math.Max(Math.Round(Finite(Cycles)), 0),
        Interval = (float)Math.Max(Finite(Interval), 0.01),
        Probability = (float)Math.Clamp(Finite(Probability), 0, 1),
    };

    private static double Finite(double v) => double.IsFinite(v) ? v : 0;

    partial void OnTimeChanged(double value) => Changed();
    partial void OnCountMinChanged(double value) => Changed();
    partial void OnCountMaxChanged(double value) => Changed();
    partial void OnCyclesChanged(double value) => Changed();
    partial void OnIntervalChanged(double value) => Changed();
    partial void OnProbabilityChanged(double value) => Changed();
}

public sealed partial class ParticleCurveKeyRowViewModel : ParticleRowViewModel
{
    [ObservableProperty] private double _time;
    [ObservableProperty] private double _value;

    public ParticleCurveKeyRowViewModel(ParticleCurveKey key, Action changed, Action<ParticleRowViewModel> remove)
        : base(changed, remove) => Apply(key);

    internal void Apply(ParticleCurveKey key)
    {
        Applying = true;
        if (Differs(Time, key.Time)) Time = key.Time;
        if (Differs(Value, key.Value)) Value = key.Value;
        Applying = false;
    }

    internal ParticleCurveKey ToModel() =>
        new((float)Math.Clamp(double.IsFinite(Time) ? Time : 0, 0, 1), (float)(double.IsFinite(Value) ? Value : 1));

    partial void OnTimeChanged(double value) => Changed();
    partial void OnValueChanged(double value) => Changed();
}

public sealed partial class ParticleGradientKeyRowViewModel : ParticleRowViewModel
{
    [ObservableProperty] private double _time;
    [ObservableProperty] private Color _color = Colors.White;

    public ParticleGradientKeyRowViewModel(ParticleGradientKey key, Action changed, Action<ParticleRowViewModel> remove)
        : base(changed, remove) => Apply(key);

    internal void Apply(ParticleGradientKey key)
    {
        Applying = true;
        if (Differs(Time, key.Time)) Time = key.Time;
        Color = ParticleSystemComponentViewModel.FromXna(key.Color);
        Applying = false;
    }

    internal ParticleGradientKey ToModel() =>
        new((float)Math.Clamp(double.IsFinite(Time) ? Time : 0, 0, 1), ParticleSystemComponentViewModel.ToXna(Color));

    partial void OnTimeChanged(double value) => Changed();
    partial void OnColorChanged(Color value) => Changed();
}

public sealed partial class ParticleSubEmitterRowViewModel : ParticleRowViewModel
{
    [ObservableProperty] private ParticleSubEmitterTrigger _trigger = ParticleSubEmitterTrigger.Death;
    [ObservableProperty] private string _target = "";
    [ObservableProperty] private double _countMin = 10;
    [ObservableProperty] private double _countMax = 10;
    [ObservableProperty] private double _inheritVelocity;
    [ObservableProperty] private bool _inheritColor;

    public ParticleSubEmitterRowViewModel(ParticleSubEmitter emitter, Action changed, Action<ParticleRowViewModel> remove)
        : base(changed, remove) => Apply(emitter);

    internal void Apply(ParticleSubEmitter emitter)
    {
        Applying = true;
        Trigger = emitter.Trigger;
        if (Target != (emitter.Target ?? "")) Target = emitter.Target ?? "";
        if (Differs(CountMin, emitter.Count.Min)) CountMin = emitter.Count.Min;
        if (Differs(CountMax, emitter.Count.Max)) CountMax = emitter.Count.Max;
        if (Differs(InheritVelocity, emitter.InheritVelocity)) InheritVelocity = emitter.InheritVelocity;
        InheritColor = emitter.InheritColor;
        Applying = false;
    }

    internal ParticleSubEmitter ToModel() => new()
    {
        Trigger = Enum.IsDefined(Trigger) ? Trigger : ParticleSubEmitterTrigger.Death,
        Target = Target ?? "",
        Count = new ParticleRange((float)Math.Max(double.IsFinite(CountMin) ? CountMin : 0, 0),
            (float)Math.Max(double.IsFinite(CountMax) ? CountMax : 0, 0)),
        InheritVelocity = (float)(double.IsFinite(InheritVelocity) ? InheritVelocity : 0),
        InheritColor = InheritColor,
    };

    partial void OnTriggerChanged(ParticleSubEmitterTrigger value)
    {
        if (Enum.IsDefined(value)) Changed();
    }
    partial void OnTargetChanged(string value) => Changed();
    partial void OnCountMinChanged(double value) => Changed();
    partial void OnCountMaxChanged(double value) => Changed();
    partial void OnInheritVelocityChanged(double value) => Changed();
    partial void OnInheritColorChanged(bool value) => Changed();
}
