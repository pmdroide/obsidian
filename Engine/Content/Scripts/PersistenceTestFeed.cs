using Engine.Editor;

namespace Engine.Scripting;

/// <summary>
/// The event feed of the PersistenceTest sample scenes: the portals and the companion orb post here, the
/// persistent player's HUD shows it (also written to the engine/editor log).
/// </summary>
public static class PersistenceTestFeed
{
    public const int MaxLines = 6;

    private static readonly List<string> Lines = new();

    /// <summary>Newest last.</summary>
    public static IReadOnlyList<string> Recent => Lines;
    /// <summary>Changes with every post, so the HUD only rebuilds its text when needed.</summary>
    public static int Version { get; private set; }

    public static void Post(string message)
    {
        Lines.Add(message);
        if (Lines.Count > MaxLines) Lines.RemoveAt(0);
        Version++;
        EditorBridge.Log("[Persistence Test] " + message);
    }

    public static void Clear()
    {
        Lines.Clear();
        Version++;
    }
}
