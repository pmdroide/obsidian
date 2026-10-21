using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Engine.Editor;
using Engine.Recources;

namespace Anvil.ViewModels;

/// <summary>One generated map shown as a thumbnail before the material is imported.</summary>
public sealed class MapPreview : IDisposable
{
    public MapPreview(string name, Bitmap image) { Name = name; Image = image; }
    public string Name { get; }
    public Bitmap Image { get; }
    public void Dispose() => Image.Dispose();
}

/// <summary>
/// Assets > Generate Material... and GameObject > Generate Material for Selection...: collects a prompt, runs the local
/// texgen generator (<see cref="TexGenRunner"/>) off the UI thread with progress and a Cancel button, shows the maps it
/// made (read straight from the cache folder, not through the engine) and imports the one the user likes through the
/// bridge (<see cref="IEditorBridge.EnqueueImportGeneratedMaterial"/>). Regenerate rolls a new seed; the dialog stays
/// open after an import, so one prompt can give several independent materials.
/// </summary>
public partial class GenerateMaterialViewModel : ViewModelBase, IDisposable
{
    public static readonly int[] Resolutions = { 256, 512, 1024, 2048 };

    private readonly IEditorBridge? _bridge;
    private readonly TexGenRunner _runner;
    private readonly int? _assignToId;
    private CancellationTokenSource? _cancel;

    /// <summary>Raised on the UI thread after an import with the new material's Content-relative path.</summary>
    public event Action<string>? Imported;

    public GenerateMaterialViewModel() : this(null, null, null) { }

    public GenerateMaterialViewModel(IEditorBridge? bridge, int? assignToId, string? assignName, TexGenRunner? runner = null)
    {
        _bridge = bridge;
        _assignToId = assignToId;
        _runner = runner ?? new TexGenRunner();
        AssignText = assignToId == null ? "" : $"The imported material is also applied to \"{assignName}\".";
        _seedText = Random.Shared.Next().ToString();
        _selectedResolution = 1024;
        Backends.Add("fake");
        _selectedBackend = "fake";
        Families.Add("auto");
        _selectedFamily = "auto";
        _statusText = "Checking the generator...";
    }

    // -------- Request --------

    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(GenerateCommand), nameof(RegenerateCommand))]
    private string _prompt = "";

    [ObservableProperty] private string _negativePrompt = "";
    [ObservableProperty] private string _seedText;
    [ObservableProperty] private int _selectedResolution;
    [ObservableProperty] private string _selectedBackend;
    [ObservableProperty] private string _selectedFamily;
    [ObservableProperty] private bool _forceDielectric;

    /// <summary>A photo or existing texture to turn into a material (classic backend); empty to use the text prompt.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasPhoto), nameof(UsesBackendChoice))]
    private string _photoPath = "";

    public ObservableCollection<string> Backends { get; } = new();
    public ObservableCollection<string> Families { get; } = new();
    public IReadOnlyList<int> ResolutionChoices => Resolutions;
    public string AssignText { get; }
    public bool HasAssignText => AssignText.Length > 0;
    public bool HasPhoto => PhotoPath.Length > 0;
    public bool UsesBackendChoice => !HasPhoto;

    // -------- State --------

    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(GenerateCommand), nameof(RegenerateCommand), nameof(ImportCommand))]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isBusy;

    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(GenerateCommand), nameof(RegenerateCommand), nameof(ImportCommand))]
    private bool _isImporting;

    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(GenerateCommand), nameof(RegenerateCommand))]
    private bool _generatorReady;

    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private string _stageText = "";
    [ObservableProperty] private string _statusText;
    [ObservableProperty] private bool _statusIsError;

    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    private string _resultDir = "";

    public bool IsIdle => !IsBusy;
    public bool HasResult => ResultDir.Length > 0 && Previews.Count > 0;
    public ObservableCollection<string> Warnings { get; } = new();
    public ObservableCollection<MapPreview> Previews { get; } = new();
    public bool HasWarnings => Warnings.Count > 0;

    /// <summary>The window sets this to open a file picker; returns the chosen file or null.</summary>
    public Func<Task<string?>>? PickPhoto { get; set; }

    // -------- Startup: which backends and families exist --------

    /// <summary>Asks texgen which backends can run and which families the style has. Call once when the dialog opens.</summary>
    public async Task InitializeAsync()
    {
        try
        {
            var availability = await _runner.CheckBackendsAsync(new[] { "fake", "sdxl_classic", "sdxl_chord" });
            var styles = await _runner.ListStylesAsync();
            Backends.Clear();
            var unavailable = new List<string>();
            foreach (var entry in availability)
            {
                if (entry.Value == null) Backends.Add(entry.Key);
                else unavailable.Add($"{entry.Key} ({entry.Value})");
            }
            Families.Clear();
            Families.Add("auto");
            foreach (string family in styles.FirstOrDefault(s => s.Key == "source2").Value ?? Array.Empty<string>())
                Families.Add(family);
            SelectedBackend = Backends.Contains(_runner.Settings.DefaultBackend) ? _runner.Settings.DefaultBackend : Backends.FirstOrDefault() ?? "";
            SelectedFamily = "auto";
            GeneratorReady = Backends.Count > 0;
            if (GeneratorReady)
                SetStatus(unavailable.Count == 0 ? "Ready." : "Ready. Not available here: " + string.Join("; ", unavailable), false);
            else
                SetStatus("The generator can't run: " + (unavailable.FirstOrDefault() ?? "no backend") +
                          ". See Docs/Research/AiTextures/ENVIRONMENT.md to set up Tools/TexGen/.venv.", true);
        }
        catch (Exception ex)
        {
            GeneratorReady = false;
            SetStatus("The generator can't run: " + ex.Message, true);
        }
    }

    // -------- Commands --------

    private bool CanGenerate() => GeneratorReady && !IsBusy && !IsImporting && Prompt.Trim().Length > 0;

    [RelayCommand(CanExecute = nameof(CanGenerate))]
    private Task GenerateAsync() => RunAsync();

    /// <summary>Generate again with a fresh seed (variations of the same prompt).</summary>
    [RelayCommand(CanExecute = nameof(CanGenerate))]
    private Task RegenerateAsync()
    {
        SeedText = Random.Shared.Next().ToString();
        return RunAsync();
    }

    [RelayCommand]
    private void RandomSeed() => SeedText = Random.Shared.Next().ToString();

    [RelayCommand]
    private void Cancel() => _cancel?.Cancel();

    [RelayCommand]
    private async Task BrowsePhotoAsync()
    {
        if (PickPhoto == null) return;
        if (await PickPhoto() is { Length: > 0 } path) PhotoPath = path;
    }

    [RelayCommand]
    private void ClearPhoto() => PhotoPath = "";

    private bool CanImport() => !IsBusy && !IsImporting && ResultDir.Length > 0 && _bridge != null;

    [RelayCommand(CanExecute = nameof(CanImport))]
    private void Import()
    {
        if (_bridge == null) return;
        IsImporting = true;
        SetStatus("Importing: building the textures with the content pipeline...", false);
        _bridge.EnqueueImportGeneratedMaterial(ResultDir, (material, error) => Dispatcher.UIThread.Post(() =>
        {
            IsImporting = false;
            if (material == null) { SetStatus("Import failed: " + (error ?? "unknown error"), true); return; }
            SetStatus($"Imported {material}. Generate again for another variation, or close.", false);
            Imported?.Invoke(material);
        }), _assignToId);
    }

    // -------- Generation --------

    private async Task RunAsync()
    {
        if (!long.TryParse(SeedText.Trim(), out long seed)) { SetStatus("The seed must be a whole number.", true); return; }
        var request = new TexGenParams
        {
            Prompt = Prompt.Trim(), NegativePrompt = NegativePrompt.Trim(), Seed = seed, Resolution = SelectedResolution,
            Backend = HasPhoto ? "classic" : SelectedBackend, Family = SelectedFamily, ForceDielectric = ForceDielectric,
            InputRgb = HasPhoto ? PhotoPath : null,
        };

        ClearResult();
        IsBusy = true;
        ProgressValue = 0;
        StageText = "starting";
        SetStatus("Generating...", false);
        _cancel = new CancellationTokenSource();
        try
        {
            var progress = new Progress<TexGenEvent>(OnEvent);
            var result = await _runner.GenerateAsync(request, progress, _cancel.Token);
            foreach (var w in result.Warnings)
                if (!Warnings.Contains(Describe(w))) Warnings.Add(Describe(w));
            OnPropertyChanged(nameof(HasWarnings));
            if (result.Cancelled) { SetStatus("Cancelled.", false); return; }
            if (!result.Success) { SetStatus($"Generation failed ({result.ErrorCode}): {result.ErrorMessage}", true); return; }
            LoadPreviews(result.CacheDir!);
            ResultDir = result.CacheDir!;
            OnPropertyChanged(nameof(HasResult));
            SetStatus(result.FromCache ? "Done (this exact request was generated before). Import it, or generate another variation."
                                       : "Done. Import it, or generate another variation.", false);
        }
        catch (Exception ex)
        {
            SetStatus("Generation failed: " + ex.Message, true);
        }
        finally
        {
            _cancel?.Dispose();
            _cancel = null;
            IsBusy = false;
            StageText = "";
        }
    }

    private void OnEvent(TexGenEvent ev)
    {
        if (ev.Kind == TexGenEventKind.Progress)
        {
            StageText = ev.Stage ?? "";
            ProgressValue = ev.Progress * 100.0;
        }
        else if (ev.Kind == TexGenEventKind.Warning)
        {
            string text = Describe(ev);
            if (!Warnings.Contains(text)) Warnings.Add(text);
            OnPropertyChanged(nameof(HasWarnings));
        }
    }

    private static string Describe(TexGenEvent warning) => $"{warning.Code}: {warning.Message}";

    private void LoadPreviews(string cacheDir)
    {
        var set = GeneratedMaterialSet.TryRead(cacheDir, out string? problem);
        if (set == null) { SetStatus("The output can't be shown: " + problem, true); return; }
        foreach (string suffix in GeneratedMaterialSet.Suffixes)
        {
            if (!set.Maps.TryGetValue(suffix, out string? file)) continue;
            try
            {
                using var stream = File.OpenRead(file);
                Previews.Add(new MapPreview(suffix == "AO" ? "AO" : suffix, Bitmap.DecodeToWidth(stream, 160)));
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException) { /* a map that can't be shown is skipped */ }
        }
    }

    private void ClearResult()
    {
        foreach (var preview in Previews) preview.Dispose();
        Previews.Clear();
        Warnings.Clear();
        ResultDir = "";
        OnPropertyChanged(nameof(HasResult));
        OnPropertyChanged(nameof(HasWarnings));
    }

    private void SetStatus(string text, bool isError)
    {
        StatusText = text;
        StatusIsError = isError;
    }

    public void Dispose()
    {
        _cancel?.Cancel();
        foreach (var preview in Previews) preview.Dispose();
        Previews.Clear();
    }
}
