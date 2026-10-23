using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Engine.Editor;
using Engine.Logic;
using Engine.Recources;

namespace Anvil.ViewModels;

/// <summary>A camera key row in the Cutscenes window (<see cref="CutsceneCameraKey"/>).</summary>
public partial class CutsceneKeyItemViewModel : ObservableObject
{
    private readonly CutscenesViewModel _root;

    [ObservableProperty] private double _time;
    [ObservableProperty] private double _fov = 45;
    [ObservableProperty] private bool _cut;
    [ObservableProperty] private bool _smooth = true;
    [ObservableProperty] private bool _isSelected;

    public Microsoft.Xna.Framework.Vector3 Position { get; set; }
    public float Yaw { get; set; }
    public float Pitch { get; set; }

    public string PoseLabel =>
        $"({Position.X:0.#}, {Position.Y:0.#}, {Position.Z:0.#})  yaw {Yaw:0}°  pitch {Pitch:0}°";

    public CutsceneKeyItemViewModel(CutscenesViewModel root, CutsceneCameraKey key)
    {
        _root = root;
        _time = Math.Round(key.Time, 2);
        _fov = key.Fov;
        _cut = key.Cut;
        _smooth = key.Smooth;
        SetPose(key);
    }

    public void SetPose(CutsceneCameraKey key)
    {
        Position = key.Position;
        Yaw = key.Yaw;
        Pitch = key.Pitch;
        OnPropertyChanged(nameof(PoseLabel));
    }

    public CutsceneCameraKey ToKey() => new()
    {
        Time = (float)Time, Position = Position, Yaw = Yaw, Pitch = Pitch, Fov = (float)Fov, Cut = Cut, Smooth = Smooth,
    };

    partial void OnTimeChanged(double value) => _root.OnTrackEdited(camera: true);
    partial void OnFovChanged(double value) => _root.OnTrackEdited(camera: true);
    partial void OnCutChanged(bool value) => _root.OnTrackEdited(camera: true);
    partial void OnSmoothChanged(bool value) => _root.OnTrackEdited(camera: true);
    partial void OnIsSelectedChanged(bool value) => _root.RaiseTimelineChanged();
}

/// <summary>An event row in the Cutscenes window (<see cref="CutsceneEvent"/>).</summary>
public partial class CutsceneEventItemViewModel : ObservableObject
{
    private readonly CutscenesViewModel _root;

    [ObservableProperty] private double _time;
    [ObservableProperty] private double _duration = 2;
    [ObservableProperty] private CutsceneEventKind _kind;
    [ObservableProperty] private string _text = "";
    [ObservableProperty] private bool _isSelected;

    public IReadOnlyList<CutsceneEventKind> Kinds => CutscenesViewModel.EventKinds;
    /// <summary>Sounds in Content/Audio for the Sound picker.</summary>
    public IReadOnlyList<string> SoundNames => _root.SoundNames;

    public string TextHint => Kind switch
    {
        CutsceneEventKind.Subtitle => "Subtitle text",
        CutsceneEventKind.Sound => "Sound file in Content/Audio (e.g. blip)",
        CutsceneEventKind.Message => "Message name for scripts (Cutscenes.EventFired)",
        _ => "(not used)",
    };

    public bool UsesText => Kind is CutsceneEventKind.Subtitle or CutsceneEventKind.Message;
    public bool IsSound => Kind == CutsceneEventKind.Sound;
    public bool IsNotSound => !IsSound;
    public bool UsesDuration => Kind is CutsceneEventKind.Subtitle or CutsceneEventKind.FadeIn or CutsceneEventKind.FadeOut;

    public CutsceneEventItemViewModel(CutscenesViewModel root, CutsceneEvent e)
    {
        _root = root;
        _time = Math.Round(e.Time, 2);
        _duration = e.Duration;
        _kind = e.Kind;
        _text = e.Text ?? "";
    }

    public CutsceneEvent ToEvent() => new() { Time = (float)Time, Duration = (float)Duration, Kind = Kind, Text = Text ?? "" };

    partial void OnTimeChanged(double value) => _root.OnTrackEdited(camera: false);
    partial void OnDurationChanged(double value) => _root.OnTrackEdited(camera: false);
    partial void OnTextChanged(string value) => _root.OnTrackEdited(camera: false);
    partial void OnIsSelectedChanged(bool value) => _root.RaiseTimelineChanged();

    partial void OnKindChanged(CutsceneEventKind value)
    {
        OnPropertyChanged(nameof(TextHint));
        OnPropertyChanged(nameof(UsesText));
        OnPropertyChanged(nameof(IsSound));
        OnPropertyChanged(nameof(IsNotSound));
        OnPropertyChanged(nameof(UsesDuration));
        _root.OnTrackEdited(camera: false);
    }
}

/// <summary>
/// Window > Cutscenes: makes <c>Content/Cutscenes/*.cutscene</c> files (<see cref="Cutscene"/>). Camera keys are
/// captured from the editor camera; scrubbing the timeline previews the shot and the overlay in the viewport
/// (<see cref="Cutscenes.Preview"/>, queued to the game thread). Play with Scene enters Play and runs it for real.
/// </summary>
public partial class CutscenesViewModel : ObservableObject
{
    public static readonly IReadOnlyList<CutsceneEventKind> EventKinds = Enum.GetValues<CutsceneEventKind>();

    private readonly IEditorBridge? _bridge;
    private readonly Func<bool> _isPlayMode;
    private readonly Action<string, bool>? _log;
    private readonly Action? _startPlayMode, _stopPlayMode;
    // Set by TickPreview: the playhead before this step, so the preview plays the Sound events it passed.
    private double? _playedFrom;
    private readonly DispatcherTimer _playTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private DateTime _lastTick;
    private bool _loading;

    public ObservableCollection<string> CutsceneNames { get; } = new();
    public ObservableCollection<CutsceneKeyItemViewModel> Keys { get; } = new();
    public ObservableCollection<CutsceneEventItemViewModel> Events { get; } = new();
    /// <summary>Sound names Audio.PlaySound takes: files in Content/Audio, ".wav" left off.</summary>
    public IReadOnlyList<string> SoundNames { get; } = ListSounds();

    [ObservableProperty] private string? _selectedName;
    [ObservableProperty] private string _newName = "";
    [ObservableProperty] private double _duration = 10;
    [ObservableProperty] private bool _letterbox = true;
    [ObservableProperty] private bool _skippable = true;
    [ObservableProperty] private bool _restoreCamera = true;
    [ObservableProperty] private double _playhead;
    [ObservableProperty] private bool _isPreviewPlaying;
    [ObservableProperty] private bool _cameraFollowsTimeline = true;
    [ObservableProperty] private bool _stopPlayWhenDone = true;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private bool _confirmingDelete;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private CutsceneKeyItemViewModel? _selectedKey;
    [ObservableProperty] private CutsceneEventItemViewModel? _selectedEvent;

    public bool HasCutscene => SelectedName != null;
    public string PlayheadLabel => $"{Playhead:0.00} s / {Duration:0.00} s";
    public string TitleLabel => SelectedName == null ? "No cutscene" : SelectedName + (IsDirty ? " *" : "");
    public string PlayButtonLabel => IsPreviewPlaying ? "Pause" : "Play";

    /// <summary>The timeline control redraws on this (keys, events, playhead or selection changed).</summary>
    public event Action? TimelineChanged;

    public CutscenesViewModel() : this(null, () => false, null) { }

    /// <param name="startPlayMode">Enters Play like the toolbar's Play button (Play with Scene).</param>
    /// <param name="stopPlayMode">Leaves Play like the toolbar's Stop button.</param>
    public CutscenesViewModel(IEditorBridge? bridge, Func<bool> isPlayMode, Action<string, bool>? log,
        Action? startPlayMode = null, Action? stopPlayMode = null)
    {
        _bridge = bridge;
        _isPlayMode = isPlayMode;
        _log = log;
        _startPlayMode = startPlayMode;
        _stopPlayMode = stopPlayMode;
        _playTimer.Tick += (_, _) => TickPreview();
        Refresh();
    }

    // ------------------------------------------------------------------
    //  Files
    // ------------------------------------------------------------------

    /// <summary>Set by the main window: hides the docked panel (the panel's x button).</summary>
    public Action? CloseAction { get; set; }

    [RelayCommand]
    private void Close() => CloseAction?.Invoke();

    /// <summary>The panel was shown again: put the preview back at the playhead.</summary>
    public void ResumePreview()
    {
        if (SelectedName != null) PushPreview(moveCamera: true);
    }

    /// <summary>Shows the cutscene at a Content path or name (Assets double-click), re-reading the folder first.</summary>
    public void Open(string nameOrPath)
    {
        string name = Path.GetFileNameWithoutExtension(nameOrPath);
        if (IsDirty && SelectedName != null && !string.Equals(SelectedName, name, StringComparison.OrdinalIgnoreCase))
            Status = $"Unsaved changes to {SelectedName} were dropped.";
        if (string.Equals(SelectedName, name, StringComparison.OrdinalIgnoreCase)) return;
        Refresh(name);
    }

    private static IReadOnlyList<string> ListSounds()
    {
        try
        {
            string root = Path.Combine(GameInfo.SourceContentRoot, "Audio");
            if (!Directory.Exists(root)) return Array.Empty<string>();
            string[] extensions = { ".wav", ".ogg", ".mp3", ".flac" };
            return Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
                .Where(f => extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                .Select(f => Path.GetRelativePath(root, f).Replace(Path.DirectorySeparatorChar, '/'))
                .Select(f => f.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ? f[..^4] : f)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch { return Array.Empty<string>(); }
    }

    private void Refresh(string? select = null)
    {
        CutsceneNames.Clear();
        foreach (string path in Cutscene.List()) CutsceneNames.Add(Path.GetFileNameWithoutExtension(path));
        SelectedName = select != null && CutsceneNames.Contains(select) ? select : CutsceneNames.FirstOrDefault();
        if (SelectedName == null) LoadInto(null);
    }

    partial void OnSelectedNameChanged(string? value)
    {
        IsPreviewPlaying = false;
        ConfirmingDelete = false;
        LoadInto(value == null ? null : Cutscene.Load(value));
        OnPropertyChanged(nameof(HasCutscene));
        OnPropertyChanged(nameof(TitleLabel));
    }

    private void LoadInto(Cutscene? cutscene)
    {
        _loading = true;
        Keys.Clear();
        Events.Clear();
        SelectedKey = null;
        SelectedEvent = null;
        if (cutscene != null)
        {
            Duration = cutscene.Duration;
            Letterbox = cutscene.Letterbox;
            Skippable = cutscene.Skippable;
            RestoreCamera = cutscene.RestoreCamera;
            foreach (var key in cutscene.SortedKeys()) Keys.Add(new CutsceneKeyItemViewModel(this, key));
            foreach (var e in cutscene.Events.OrderBy(e => e.Time)) Events.Add(new CutsceneEventItemViewModel(this, e));
        }
        Playhead = 0;
        _loading = false;
        IsDirty = false;
        if (cutscene != null) PushPreview(moveCamera: Keys.Count > 0);
        else EndPreview();
        RaiseTimelineChanged();
    }

    /// <summary>The cutscene as edited (a fresh object: the game thread keeps its own copy).</summary>
    public Cutscene Build() => new()
    {
        Path = SelectedName == null ? null : Cutscene.Normalize(SelectedName),
        Duration = (float)Math.Max(0.1, Duration),
        Letterbox = Letterbox,
        Skippable = Skippable,
        RestoreCamera = RestoreCamera,
        CameraKeys = Keys.OrderBy(k => k.Time).Select(k => k.ToKey()).ToList(),
        Events = Events.OrderBy(e => e.Time).Select(e => e.ToEvent()).ToList(),
    };

    [RelayCommand]
    private void New()
    {
        string name = string.Join("_", (NewName ?? "").Trim().Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrWhiteSpace(name))
        {
            Status = "Type a name for the new cutscene first.";
            return;
        }
        if (CutsceneNames.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
        {
            Status = $"A cutscene called {name} already exists.";
            return;
        }
        try
        {
            var cutscene = new Cutscene();
            cutscene.Events.Add(new CutsceneEvent { Time = 0, Duration = 1, Kind = CutsceneEventKind.FadeIn });
            cutscene.Save(name);
            NewName = "";
            Refresh(name);
            // Start from where the editor camera is looking, so Play shows something straight away.
            string created = $"Created Cutscenes/{name}{Cutscene.Extension}. Untick Camera follows timeline to fly the viewport camera, move the playhead, then + Key from View.";
            Status = created;
            CaptureKey(0, replaceSelected: false, done: () =>
            {
                Save();
                Status = created;
            });
            _log?.Invoke($"Cutscene created: Cutscenes/{name}{Cutscene.Extension}", false);
        }
        catch (Exception ex)
        {
            Status = "Couldn't create the cutscene: " + ex.Message;
        }
    }

    [RelayCommand]
    private void Save()
    {
        if (SelectedName == null) return;
        try
        {
            string rel = Build().Save();
            IsDirty = false;
            Status = $"Saved {rel}.";
            _log?.Invoke($"Cutscene saved: {rel} ({Keys.Count} camera keys, {Events.Count} events)", false);
        }
        catch (Exception ex)
        {
            Status = "Couldn't save: " + ex.Message;
            _log?.Invoke("Couldn't save cutscene: " + ex.Message, true);
        }
    }

    [RelayCommand]
    private void Revert()
    {
        if (SelectedName == null) return;
        LoadInto(Cutscene.Load(SelectedName));
        Status = "Reverted to the saved file.";
    }

    [RelayCommand]
    private void Delete()
    {
        if (SelectedName == null) return;
        if (!ConfirmingDelete)
        {
            ConfirmingDelete = true;
            Status = $"Press Delete again to remove {SelectedName} for good.";
            return;
        }
        ConfirmingDelete = false;
        string rel = Cutscene.Normalize(SelectedName)!;
        try
        {
            string full = Path.Combine(GameInfo.SourceContentRoot, rel.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(full)) File.Delete(full);
            string built = Path.Combine(AppContext.BaseDirectory, "Content", rel.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(built)) File.Delete(built);
            Status = $"Deleted {rel}.";
            _log?.Invoke($"Cutscene deleted: {rel}", false);
            Refresh();
        }
        catch (Exception ex)
        {
            Status = "Couldn't delete: " + ex.Message;
        }
    }

    partial void OnDurationChanged(double value) => OnSettingChanged();
    partial void OnLetterboxChanged(bool value) => OnSettingChanged();
    partial void OnSkippableChanged(bool value) => OnSettingChanged();
    partial void OnRestoreCameraChanged(bool value) => OnSettingChanged();
    partial void OnIsDirtyChanged(bool value) => OnPropertyChanged(nameof(TitleLabel));
    partial void OnIsPreviewPlayingChanged(bool value) => OnPropertyChanged(nameof(PlayButtonLabel));

    private void OnSettingChanged()
    {
        OnPropertyChanged(nameof(PlayheadLabel));
        if (_loading) return;
        IsDirty = true;
        if (Playhead > Duration) Playhead = Duration;
        PushPreview(moveCamera: false);
        RaiseTimelineChanged();
    }

    // ------------------------------------------------------------------
    //  Camera keys
    // ------------------------------------------------------------------

    /// <summary>Adds a key at the playhead from the viewport camera (or re-poses the key already there).</summary>
    [RelayCommand]
    private void AddKeyFromView() => CaptureKey(Playhead, replaceSelected: false);

    /// <summary>Re-poses the selected key from the viewport camera, keeping its time.</summary>
    [RelayCommand]
    private void SetKeyFromView()
    {
        if (SelectedKey != null) CaptureKey(SelectedKey.Time, replaceSelected: true);
    }

    private void CaptureKey(double time, bool replaceSelected, Action? done = null)
    {
        if (_bridge == null || SelectedName == null) return;
        var target = replaceSelected ? SelectedKey : null;
        _bridge.EnqueueGameThreadAction(() =>
        {
            var key = Cutscenes.CaptureEditorCamera((float)time);
            if (key == null) return;
            Dispatcher.UIThread.Post(() =>
            {
                var existing = target ?? Keys.FirstOrDefault(k => Math.Abs(k.Time - time) < 0.01);
                if (existing != null)
                {
                    existing.SetPose(key);
                    existing.Fov = key.Fov;
                    SelectKey(existing);
                    Status = $"Key at {existing.Time:0.00} s set from the viewport camera.";
                }
                else
                {
                    var item = new CutsceneKeyItemViewModel(this, key);
                    int index = Keys.TakeWhile(k => k.Time < item.Time).Count();
                    Keys.Insert(index, item);
                    SelectKey(item);
                    if (item.Time > Duration) Duration = Math.Ceiling(item.Time);
                    Status = $"Camera key added at {item.Time:0.00} s.";
                }
                IsDirty = true;
                RaiseTimelineChanged();
                done?.Invoke();
            });
        });
    }

    [RelayCommand]
    private void RemoveKey(CutsceneKeyItemViewModel? key)
    {
        key ??= SelectedKey;
        if (key == null) return;
        Keys.Remove(key);
        if (SelectedKey == key) SelectedKey = null;
        OnTrackEdited(camera: true);
    }

    [RelayCommand]
    private void GoToKey(CutsceneKeyItemViewModel? key)
    {
        if (key == null) return;
        SelectKey(key);
        Playhead = key.Time;
    }

    public void SelectKey(CutsceneKeyItemViewModel? key)
    {
        SelectedKey = key;
        if (key != null) SelectedEvent = null;
    }

    partial void OnSelectedKeyChanged(CutsceneKeyItemViewModel? oldValue, CutsceneKeyItemViewModel? newValue)
    {
        if (oldValue != null) oldValue.IsSelected = false;
        if (newValue != null) newValue.IsSelected = true;
    }

    // ------------------------------------------------------------------
    //  Events
    // ------------------------------------------------------------------

    [RelayCommand]
    private void AddEvent(string? kind)
    {
        if (SelectedName == null) return;
        var parsed = Enum.TryParse(kind, out CutsceneEventKind k) ? k : CutsceneEventKind.Subtitle;
        var item = new CutsceneEventItemViewModel(this, new CutsceneEvent
        {
            Time = (float)Math.Round(Playhead, 2),
            Duration = parsed == CutsceneEventKind.Subtitle ? 3f : 1f,
            Kind = parsed,
            Text = parsed switch
            {
                CutsceneEventKind.Subtitle => "New subtitle",
                CutsceneEventKind.Sound => SoundNames.FirstOrDefault() ?? "",
                _ => "",
            },
        });
        int index = Events.TakeWhile(e => e.Time <= item.Time).Count();
        Events.Insert(index, item);
        SelectEvent(item);
        OnTrackEdited(camera: false);
    }

    [RelayCommand]
    private void RemoveEvent(CutsceneEventItemViewModel? item)
    {
        item ??= SelectedEvent;
        if (item == null) return;
        Events.Remove(item);
        if (SelectedEvent == item) SelectedEvent = null;
        OnTrackEdited(camera: false);
    }

    public void SelectEvent(CutsceneEventItemViewModel? item)
    {
        SelectedEvent = item;
        if (item != null) SelectedKey = null;
    }

    partial void OnSelectedEventChanged(CutsceneEventItemViewModel? oldValue, CutsceneEventItemViewModel? newValue)
    {
        if (oldValue != null) oldValue.IsSelected = false;
        if (newValue != null) newValue.IsSelected = true;
    }

    /// <summary>A key or event changed: mark unsaved, redraw the timeline, refresh the preview.</summary>
    public void OnTrackEdited(bool camera)
    {
        if (_loading) return;
        IsDirty = true;
        PushPreview(moveCamera: camera);
        RaiseTimelineChanged();
    }

    public void RaiseTimelineChanged() => TimelineChanged?.Invoke();

    // ------------------------------------------------------------------
    //  Preview
    // ------------------------------------------------------------------

    partial void OnPlayheadChanged(double value)
    {
        double clamped = Math.Clamp(value, 0, Math.Max(0, Duration));
        if (clamped != value)
        {
            Playhead = clamped;
            return;
        }
        OnPropertyChanged(nameof(PlayheadLabel));
        if (!_loading) PushPreview(moveCamera: true);
        RaiseTimelineChanged();
    }

    partial void OnCameraFollowsTimelineChanged(bool value)
    {
        if (value) PushPreview(moveCamera: true);
    }

    [RelayCommand]
    private void TogglePlay()
    {
        if (SelectedName == null) return;
        IsPreviewPlaying = !IsPreviewPlaying;
        if (IsPreviewPlaying)
        {
            if (Playhead >= Duration - 1e-3) Playhead = 0;
            _lastTick = DateTime.UtcNow;
            // Sounds placed exactly at the starting time play too.
            _playedFrom = Playhead - 1e-4;
            PushPreview(moveCamera: true);
            _playedFrom = null;
            _playTimer.Start();
        }
        else StopPlayback();
    }

    [RelayCommand]
    private void Rewind()
    {
        Playhead = 0;
    }

    private void TickPreview()
    {
        DateTime now = DateTime.UtcNow;
        double dt = (now - _lastTick).TotalSeconds;
        _lastTick = now;
        if (!IsPreviewPlaying || SelectedName == null)
        {
            _playTimer.Stop();
            return;
        }
        double next = Playhead + dt;
        bool ended = next >= Duration;
        if (ended) next = Duration;
        _playedFrom = Playhead;
        Playhead = next;
        _playedFrom = null;
        if (ended)
        {
            IsPreviewPlaying = false;
            _playTimer.Stop();
        }
    }

    private void StopPlayback()
    {
        _playTimer.Stop();
        _bridge?.EnqueueGameThreadAction(Cutscenes.StopPreviewSounds);
    }

    /// <summary>
    /// Enters Play (scripts, physics, Audio components) and plays the edited cutscene on the game camera, with its
    /// sounds and messages; Stop Play When Done leaves Play when it ends.
    /// </summary>
    [RelayCommand]
    private void PlayWithScene()
    {
        if (_bridge == null || SelectedName == null) return;
        if (IsPreviewPlaying) TogglePlay();
        bool started = false;
        if (!_isPlayMode())
        {
            if (_startPlayMode == null) return;
            _startPlayMode();
            started = true;
        }
        var cutscene = Build();
        bool stopAfter = started && StopPlayWhenDone;
        // Queued after the Play request, so it runs once Play has started the scene's scripts.
        _bridge.EnqueueGameThreadAction(() =>
        {
            Action<Cutscene>? onFinished = null;
            onFinished = ended =>
            {
                if (ended != cutscene) return;
                Cutscenes.Finished -= onFinished;
                if (stopAfter) Dispatcher.UIThread.Post(() => { if (_isPlayMode()) _stopPlayMode?.Invoke(); });
            };
            Cutscenes.Finished += onFinished;
            if (!Cutscenes.Play(cutscene)) Cutscenes.Finished -= onFinished;
        });
        Status = started
            ? "Playing the scene with the cutscene" + (stopAfter ? " (Play stops when it ends)." : ".") + " Enter/Space skips when Skippable."
            : "Playing the cutscene in game. Enter/Space skips when Skippable.";
    }

    private void PushPreview(bool moveCamera)
    {
        if (_bridge == null || SelectedName == null) return;
        var cutscene = Build();
        float time = (float)Playhead;
        bool move = moveCamera && CameraFollowsTimeline && !_isPlayMode();
        float? from = _playedFrom is { } f ? (float)f : null;
        _bridge.EnqueueGameThreadAction(() => Cutscenes.Preview(cutscene, time, move, from));
    }

    /// <summary>Window closed: stop playback and clear the viewport overlay.</summary>
    public void EndPreview()
    {
        _playTimer.Stop();
        IsPreviewPlaying = false;
        _bridge?.EnqueueGameThreadAction(Cutscenes.EndPreview);
    }
}
