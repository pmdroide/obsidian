using System;
using System.IO;
using System.Threading.Tasks;
using Anvil.Services;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Engine.Recources;

namespace Anvil.ViewModels;

/// <summary>
/// Backs the Game Settings dialog: the standalone game's window title, icon, size,
/// resizability, fullscreen, VSync and FPS cap.
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

    public GameSettingsViewModel()
    {
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
    private void ClearIcon()
    {
        _pendingIcon = null;
        _iconCleared = true;
        IconPreview = null;
        IconLabel = "Default (executable icon)";
    }

    /// <summary>
    /// Writes GameInfo.json (and the converted .ico when a new image was picked) into
    /// Engine/Content. Returns the saved title. Must run on the UI thread.
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
        return title;
    }

    private static Bitmap? TryLoad(string path)
    {
        try { return new Bitmap(path); }
        catch { return null; }
    }
}
