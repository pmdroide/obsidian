using System;
using System.IO;
using Avalonia.Threading;
using Engine.Components;
using Engine.Recources;

namespace Anvil.Models;

/// <summary>
/// Inspector for a material asset file (<see cref="MaterialAsset"/>, a .mat in Content) selected in the Assets panel.
/// It reuses the Material component's editor: edits go into a local copy of the surface and are written to the file
/// shortly after, and the engine then updates every gameobject linked to it (<see cref="MaterialAsset.SyncScene"/>).
/// </summary>
public sealed class MaterialAssetViewModel : MaterialInfo
{
    private MaterialComponent _settings = new();
    private bool _loading;
    private DateTime _lastWrite;
    private readonly DispatcherTimer _saveTimer;

    /// <summary>Content-relative path of the file ("Materials/Brick.mat").</summary>
    public string Path { get; }
    public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);
    private string FullPath => System.IO.Path.Combine(MaterialAsset.SourceRoot, Path.Replace('/', System.IO.Path.DirectorySeparatorChar));

    /// <summary>Raised after a save fails (message), so the window can log it.</summary>
    public event Action<string>? SaveFailed;

    // The editor needs an owner; a detached one has no engine object, so nothing reaches the bridge through it.
    public MaterialAssetViewModel(string path) : base(new SceneObjectViewModel())
    {
        Path = path;
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _saveTimer.Tick += (_, _) => Save();
        Reload();
    }

    /// <summary>False when the file can't be read (deleted or damaged).</summary>
    public bool Reload()
    {
        var asset = MaterialAsset.Load(Path);
        if (asset == null) return false;
        _settings = asset.CreateSettings();
        _lastWrite = WriteTime();
        _loading = true;
        try { Apply(_settings, freezeFields: false); }
        finally { _loading = false; }
        return true;
    }

    /// <summary>Re-reads the file after an outside edit (text editor, a linked gameobject's Inspector); false when it's gone.</summary>
    public bool ReloadIfChanged()
    {
        if (!File.Exists(FullPath)) return false;
        if (_saveTimer.IsEnabled || WriteTime() == _lastWrite) return true;
        return Reload();
    }

    protected override void Push(Action<GameComponent> mutate)
    {
        if (_loading) return;
        mutate(_settings);
        // Slider drags change many values a second: write once they settle.
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>Writes pending edits now (also before switching away).</summary>
    public void Save()
    {
        _saveTimer.Stop();
        try
        {
            MaterialAsset.Write(Path, _settings);
            _lastWrite = WriteTime();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            SaveFailed?.Invoke($"Couldn't save {Path}: {ex.Message}");
        }
    }

    public void Flush()
    {
        if (_saveTimer.IsEnabled) Save();
    }

    private DateTime WriteTime()
    {
        try { return File.GetLastWriteTimeUtc(FullPath); }
        catch (IOException) { return default; }
    }
}
