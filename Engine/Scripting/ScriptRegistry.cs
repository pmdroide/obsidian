namespace Engine.Scripting;

public sealed record ScriptDefinition(string Id, string DisplayName, Func<ScriptBehaviour> Create);

/// <summary>Stable script IDs are saved in scenes; register compiled C# behaviours here.</summary>
public static class ScriptRegistry
{
    private static readonly Dictionary<string, ScriptDefinition> Definitions = new();

    static ScriptRegistry()
    {
        Register<SpinExampleScript>(SpinExampleScript.ScriptId, "Spin Example");
        Register<FreecamScript>(FreecamScript.ScriptId, "Freecam");
        Register<MainMenuScript>(MainMenuScript.ScriptId, "Main Menu");
    }

    public static IReadOnlyCollection<ScriptDefinition> All => Definitions.Values;

    public static void Register<T>(string id, string displayName) where T : ScriptBehaviour, new()
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Script id is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("Script name is required.", nameof(displayName));
        Definitions.Add(id, new ScriptDefinition(id, displayName, () => new T()));
    }

    public static ScriptDefinition Find(string id) =>
        id != null && Definitions.TryGetValue(id, out var definition) ? definition : null;
}
