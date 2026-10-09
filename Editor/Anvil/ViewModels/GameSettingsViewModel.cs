using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Anvil.Services;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Engine.Recources;

namespace Anvil.ViewModels;

/// <summary>One row of Game Settings > Scenes: a Content-relative .obsc in build order.</summary>
public partial class SceneListEntryViewModel : ObservableObject
{
    [ObservableProperty] private int _index;
    public string Entry { get; }
    public string Name => SceneList.DisplayName(Entry);
    public bool Exists { get; }
    public string IndexLabel => Index.ToString("00");
    public string Note => !Exists ? "Missing" : Index == 0 ? "Loads first" : "";

    public SceneListEntryViewModel(string entry, int index)
    {
        Entry = entry;
        _index = index;
        Exists = File.Exists(SceneList.ResolvePath(entry));
    }

    partial void OnIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IndexLabel));
        OnPropertyChanged(nameof(Note));
    }
}

/// <summary>One row of Game Settings > Intro Videos: a Content-relative video in play order.</summary>
public partial class IntroVideoEntryViewModel : ObservableObject
{
    [ObservableProperty] private int _index;
    public string Entry { get; }
    public string Name => IntroVideoList.DisplayName(Entry);
    public bool Exists { get; }
    public string IndexLabel => Index.ToString("00");
    public string Note => !Exists ? "Missing" : Index == 0 ? "Plays first" : "";

    public IntroVideoEntryViewModel(string entry, int index)
    {
        Entry = entry;
        _index = index;
        Exists = File.Exists(IntroVideoList.ResolvePath(entry));
    }

    partial void OnIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IndexLabel));
        OnPropertyChanged(nameof(Note));
    }
}

/// <summary>
/// Backs the Game Settings dialog: the standalone game's window title, icon, size,
/// resizability, fullscreen, VSync and FPS cap, the intro videos (play order) and the
/// scene list (build order).
/// Nothing is written until <see cref="Apply"/> runs (dialog confirmed); the picked image
/// is only decoded for the preview until then.
/// </summary>
public partial class GameSettingsViewModel : ViewModelBase
{
    [ObservableProperty] private string _windowTitle = "Engine";

    // decimal = NumericUpDown.Value's type, so the compiled bindings need no converter.
    [ObservableProperty] private decimal _windowWidth = 1280;
    [ObservableProperty] private decimal _windowHeight = 720;
    [ObservableProperty] private bool _allowResizing = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWindowed))]
    private bool _fullscreen;

    public bool IsWindowed => !Fullscreen;

    [ObservableProperty] private bool _vSync;
    [ObservableProperty] private decimal _fpsCap;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIcon), nameof(HasNoIcon))]
    private Bitmap? _iconPreview;

    [ObservableProperty] private string _iconLabel = "Default (executable icon)";

    public bool HasIcon => IconPreview != null;
    public bool HasNoIcon => IconPreview == null;

    // Picked-but-not-yet-saved image; null with _iconCleared=false means "keep current".
    private Bitmap? _pendingIcon;
    private bool _iconCleared;
    private string? _existingIconPath;

    // -------- Scenes (Content/System/SceneList.json) --------

    /// <summary>Scenes in build order. Entry 0 is the one the standalone game loads first.</summary>
    public ObservableCollection<SceneListEntryViewModel> Scenes { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveSceneCommand), nameof(MoveSceneUpCommand), nameof(MoveSceneDownCommand))]
    private SceneListEntryViewModel? _selectedScene;

    [ObservableProperty] private string _sceneListMessage = "";

    public bool HasNoScenes => Scenes.Count == 0;

    // -------- Intro videos (Content/System/IntroVideos.json) --------

    /// <summary>Videos the standalone game plays before scene 0, in order. Enter skips one.</summary>
    public ObservableCollection<IntroVideoEntryViewModel> IntroVideos { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveIntroVideoCommand), nameof(MoveIntroVideoUpCommand), nameof(MoveIntroVideoDownCommand))]
    private IntroVideoEntryViewModel? _selectedIntroVideo;

    [ObservableProperty] private string _introVideoMessage = "";

    public bool HasNoIntroVideos => IntroVideos.Count == 0;

    public GameSettingsViewModel()
    {
        foreach (string entry in IntroVideoList.Read().Videos)
            IntroVideos.Add(new IntroVideoEntryViewModel(entry, IntroVideos.Count));
        IntroVideos.CollectionChanged += (_, _) =>
        {
            for (int i = 0; i < IntroVideos.Count; i++) IntroVideos[i].Index = i;
            OnPropertyChanged(nameof(HasNoIntroVideos));
        };

        foreach (string entry in SceneList.Read().Scenes)
            Scenes.Add(new SceneListEntryViewModel(entry, Scenes.Count));
        Scenes.CollectionChanged += (_, _) =>
        {
            for (int i = 0; i < Scenes.Count; i++) Scenes[i].Index = i;
            OnPropertyChanged(nameof(HasNoScenes));
        };

        GameInfo.Data data = GameInfo.Read();
        WindowTitle = data.WindowTitle ?? "Engine";
        WindowWidth = data.WindowWidth > 0 ? data.WindowWidth : 1280;
        WindowHeight = data.WindowHeight > 0 ? data.WindowHeight : 720;
        AllowResizing = data.AllowResizing;
        Fullscreen = data.Fullscreen;
        VSync = data.VSync;
        FpsCap = Math.Max(0, data.FpsCap);
        _existingIconPath = data.IconPath;

        string? iconFile = string.IsNullOrWhiteSpace(data.IconPath)
            ? null
            : Path.Combine(GameInfo.ResolveContentRoot(), data.IconPath);
        if (iconFile != null && File.Exists(iconFile))
        {
            IconPreview = TryLoad(iconFile);
            if (IconPreview != null) IconLabel = data.IconPath!;
        }
    }

    [RelayCommand]
    private async Task BrowseIconAsync(Window? window)
    {
        if (window?.StorageProvider is not { } sp) return;
        var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose Game Icon",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Images")
                {
                    Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.ico", "*.gif", "*.webp" },
                },
            },
        });
        if (files.Count == 0) return;

        string path = files[0].Path.LocalPath;
        Bitmap? bmp = TryLoad(path);
        if (bmp == null)
        {
            IconLabel = $"Could not read image: {Path.GetFileName(path)}";
            return;
        }

        _pendingIcon = bmp;
        _iconCleared = false;
        IconPreview = bmp;
        IconLabel = Path.GetFileName(path);
    }

    [RelayCommand]
    private async Task AddScenesAsync(Window? window)
    {
        if (window?.StorageProvider is not { } sp) return;
        IStorageFolder? start = null;
        try { start = await sp.TryGetFolderFromPathAsync(new Uri(Path.Combine(GameInfo.SourceContentRoot, "Scenes"))); }
        catch { /* no Scenes folder yet */ }
        var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Add Scenes",
            AllowMultiple = true,
            SuggestedStartLocation = start,
            FileTypeFilter = new[] { new FilePickerFileType("Obsidian Scene") { Patterns = new[] { "*.obsc" } } },
        });

        var outside = new System.Collections.Generic.List<string>();
        foreach (var file in files)
        {
            string? entry = SceneList.ToEntry(file.Path.LocalPath);
            if (entry == null) { outside.Add(Path.GetFileName(file.Path.LocalPath)); continue; }
            if (Scenes.Any(s => string.Equals(s.Entry, entry, StringComparison.OrdinalIgnoreCase))) continue;
            Scenes.Add(new SceneListEntryViewModel(entry, Scenes.Count));
            SelectedScene = Scenes[^1];
        }
        SceneListMessage = outside.Count == 0 ? ""
            : $"Not added (outside Engine/Content): {string.Join(", ", outside)}. Save scenes under Content/Scenes.";
    }

    private bool HasSelectedScene => SelectedScene != null;

    [RelayCommand(CanExecute = nameof(HasSelectedScene))]
    private void RemoveScene()
    {
        if (SelectedScene is not { } scene) return;
        int index = Scenes.IndexOf(scene);
        Scenes.Remove(scene);
        SelectedScene = Scenes.Count == 0 ? null : Scenes[Math.Min(index, Scenes.Count - 1)];
    }

    private bool CanMoveSceneUp => SelectedScene != null && Scenes.IndexOf(SelectedScene) > 0;
    private bool CanMoveSceneDown => SelectedScene != null && Scenes.IndexOf(SelectedScene) < Scenes.Count - 1;

    [RelayCommand(CanExecute = nameof(CanMoveSceneUp))]
    private void MoveSceneUp() => MoveSelectedScene(-1);

    [RelayCommand(CanExecute = nameof(CanMoveSceneDown))]
    private void MoveSceneDown() => MoveSelectedScene(1);

    private void MoveSelectedScene(int delta)
    {
        if (SelectedScene is not { } scene) return;
        int index = Scenes.IndexOf(scene);
        Scenes.Move(index, index + delta);
        SelectedScene = scene;
        MoveSceneUpCommand.NotifyCanExecuteChanged();
        MoveSceneDownCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task AddIntroVideosAsync(Window? window)
    {
        if (window?.StorageProvider is not { } sp) return;
        IStorageFolder? start = null;
        try { start = await sp.TryGetFolderFromPathAsync(new Uri(Path.Combine(GameInfo.SourceContentRoot, "Video"))); }
        catch { /* no Video folder yet */ }
        var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Add Intro Videos",
            AllowMultiple = true,
            SuggestedStartLocation = start,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Video") { Patterns = IntroVideoList.Extensions.Select(e => "*" + e).ToArray() },
            },
        });

        var outside = new System.Collections.Generic.List<string>();
        foreach (var file in files)
        {
            string? entry = IntroVideoList.ToEntry(file.Path.LocalPath);
            if (entry == null) { outside.Add(Path.GetFileName(file.Path.LocalPath)); continue; }
            if (IntroVideos.Any(v => string.Equals(v.Entry, entry, StringComparison.OrdinalIgnoreCase))) continue;
            IntroVideos.Add(new IntroVideoEntryViewModel(entry, IntroVideos.Count));
            SelectedIntroVideo = IntroVideos[^1];
        }
        IntroVideoMessage = outside.Count == 0 ? ""
            : $"Not added (outside Engine/Content): {string.Join(", ", outside)}. Put videos under Content/Video.";
    }

    private bool HasSelectedIntroVideo => SelectedIntroVideo != null;

    [RelayCommand(CanExecute = nameof(HasSelectedIntroVideo))]
    private void RemoveIntroVideo()
    {
        if (SelectedIntroVideo is not { } video) return;
        int index = IntroVideos.IndexOf(video);
        IntroVideos.Remove(video);
        SelectedIntroVideo = IntroVideos.Count == 0 ? null : IntroVideos[Math.Min(index, IntroVideos.Count - 1)];
    }

    private bool CanMoveIntroVideoUp => SelectedIntroVideo != null && IntroVideos.IndexOf(SelectedIntroVideo) > 0;
    private bool CanMoveIntroVideoDown => SelectedIntroVideo != null && IntroVideos.IndexOf(SelectedIntroVideo) < IntroVideos.Count - 1;

    [RelayCommand(CanExecute = nameof(CanMoveIntroVideoUp))]
    private void MoveIntroVideoUp() => MoveSelectedIntroVideo(-1);

    [RelayCommand(CanExecute = nameof(CanMoveIntroVideoDown))]
    private void MoveIntroVideoDown() => MoveSelectedIntroVideo(1);

    private void MoveSelectedIntroVideo(int delta)
    {
        if (SelectedIntroVideo is not { } video) return;
        int index = IntroVideos.IndexOf(video);
        IntroVideos.Move(index, index + delta);
        SelectedIntroVideo = video;
        MoveIntroVideoUpCommand.NotifyCanExecuteChanged();
        MoveIntroVideoDownCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void ClearIcon()
    {
        _pendingIcon = null;
        _iconCleared = true;
        IconPreview = null;
        IconLabel = "Default (executable icon)";
    }

    /// <summary>
    /// Writes GameInfo.json (and the converted .ico when a new image was picked),
    /// IntroVideos.json and SceneList.json into Engine/Content. Returns the saved title. Must run on the UI thread.
    /// The running engine picks up the new scene list immediately (same process).
    /// </summary>
    public string Apply()
    {
        string title = string.IsNullOrWhiteSpace(WindowTitle) ? "Engine" : WindowTitle.Trim();
        string? iconPath = _iconCleared ? null : _existingIconPath;

        if (_pendingIcon != null)
        {
            iconPath = GameInfo.DefaultIconPath;
            IconWriter.WriteIco(_pendingIcon,
                Path.Combine(GameInfo.SourceContentRoot, iconPath.Replace('/', Path.DirectorySeparatorChar)));
        }

        GameInfo.Save(new GameInfo.Data
        {
            WindowTitle = title,
            IconPath = iconPath,
            WindowWidth = Math.Max(1, (int)WindowWidth),
            WindowHeight = Math.Max(1, (int)WindowHeight),
            AllowResizing = AllowResizing,
            Fullscreen = Fullscreen,
            VSync = VSync,
            FpsCap = Math.Max(0, (int)FpsCap),
        });
        IntroVideoList.Save(new IntroVideoList.Data { Videos = IntroVideos.Select(v => v.Entry).ToList() });
        SceneList.Save(new SceneList.Data { Scenes = Scenes.Select(s => s.Entry).ToList() });
        return title;
    }

    private static Bitmap? TryLoad(string path)
    {
        try { return new Bitmap(path); }
        catch { return null; }
    }
}
