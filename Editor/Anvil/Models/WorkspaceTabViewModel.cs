namespace Anvil.Models;

/// <summary>A workspace panel identity and its tab title.</summary>
public sealed class WorkspaceTabViewModel
{
    public string Title { get; }
    public string Kind { get; }
    public bool IsEngine => Kind == "Engine";

    public WorkspaceTabViewModel(string kind, string title)
    {
        Kind = kind;
        Title = title;
    }
}
