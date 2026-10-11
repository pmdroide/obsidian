using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Engine.Recources;

namespace Anvil.ViewModels;

/// <summary>
/// Edit > Project Settings: the project's name and Content folder, and the Delete Project
/// action. The window only collects the confirmation: it closes with <c>true</c> when the user
/// confirmed, and <see cref="MainWindowViewModel"/> runs the reset through the bridge.
/// </summary>
public partial class ProjectSettingsViewModel : ViewModelBase
{
    /// <summary>Typed to confirm when the project has no name.</summary>
    public const string FallbackConfirmation = "DELETE";

    public ProjectSettingsViewModel() : this(isPlaying: false) { }

    public ProjectSettingsViewModel(bool isPlaying)
    {
        ProjectName = GameInfo.Read().WindowTitle ?? "";
        ContentRoot = GameInfo.SourceContentRoot;
        SceneCount = SceneList.Read().Scenes.Count;
        CanDelete = !isPlaying;
        try
        {
            foreach (string rel in ProjectReset.Plan(ContentRoot))
            {
                PlannedRemovals.Add(rel);
                string full = Path.Combine(ContentRoot, rel.Replace('/', Path.DirectorySeparatorChar));
                PlannedFileCount += Directory.Exists(full)
                    ? Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories).Count()
                    : 1;
            }
        }
        catch (Exception ex)
        {
            PlanError = "Couldn't list the project's content: " + ex.Message;
        }
    }

    public string ProjectName { get; }
    public string ContentRoot { get; }
    public int SceneCount { get; }

    /// <summary>False during Play: Stop would swap the old scene back in over the new project.</summary>
    public bool CanDelete { get; }
    public bool IsPlayBlocking => !CanDelete;

    /// <summary>Top-most Content-relative files and folders the reset removes (<see cref="ProjectReset.Plan"/>).</summary>
    public ObservableCollection<string> PlannedRemovals { get; } = new();
    public int PlannedFileCount { get; }
    public bool HasPlannedRemovals => PlannedRemovals.Count > 0;
    public string? PlanError { get; }
    public bool HasPlanError => PlanError != null;

    public string PlanSummary => PlannedRemovals.Count == 0
        ? "The project has no content to remove; only the settings would be reset."
        : $"{PlannedFileCount} file{(PlannedFileCount == 1 ? "" : "s")} in {PlannedRemovals.Count} item{(PlannedRemovals.Count == 1 ? "" : "s")} will be removed:";

    /// <summary>What the user types to confirm: the project name, or <see cref="FallbackConfirmation"/>.</summary>
    public string ConfirmationPhrase => string.IsNullOrWhiteSpace(ProjectName) ? FallbackConfirmation : ProjectName.Trim();
    public string ConfirmationPrompt => $"Type \"{ConfirmationPhrase}\" to confirm.";

    [ObservableProperty]
    private bool _isConfirmingDelete;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfirmationValid))]
    private string _confirmationText = "";

    public bool IsConfirmationValid =>
        CanDelete && string.Equals((ConfirmationText ?? "").Trim(), ConfirmationPhrase, StringComparison.Ordinal);

    [RelayCommand]
    private void BeginDelete()
    {
        if (!CanDelete) return;
        ConfirmationText = "";
        IsConfirmingDelete = true;
    }

    [RelayCommand]
    private void CancelDelete()
    {
        ConfirmationText = "";
        IsConfirmingDelete = false;
    }
}
