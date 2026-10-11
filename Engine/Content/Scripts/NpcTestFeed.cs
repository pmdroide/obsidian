using Engine.Editor;

namespace Engine.Scripting;

/// <summary>
/// The event feed of the NpcTest sample scene: the NPCs' reporter scripts and the player post here, the
/// player's HUD shows it (also written to the engine/editor log).
/// </summary>
public static class NpcTestFeed
{
    public const int MaxLines = 7;

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
        EditorBridge.Log("[NPC Test] " + message);
    }

    public static void Clear()
    {
        Lines.Clear();
        Version++;
    }
}
