namespace Engine.Scripting;

/// <param name="ScriptType">The behaviour's C# type; its serialized fields come from <see cref="ScriptFields"/>.</param>
public sealed record ScriptDefinition(string Id, string DisplayName, Func<ScriptBehaviour> Create, Type ScriptType = null)
{
    public IReadOnlyList<ScriptFieldInfo> Fields => ScriptFields.For(ScriptType);
}

/// <summary>Stable script IDs are saved in scenes; register compiled C# behaviours here.</summary>
public static class ScriptRegistry
{
    private static readonly Dictionary<string, ScriptDefinition> Definitions = new();

    static ScriptRegistry()
    {
        Register<SpinExampleScript>(SpinExampleScript.ScriptId, "Spin Example");
        Register<FreecamScript>(FreecamScript.ScriptId, "Freecam");
        Register<MainMenuScript>(MainMenuScript.ScriptId, "Main Menu");
        Register<MultiplayerTestScript>(MultiplayerTestScript.ScriptId, "Multiplayer Test");
        Register<CollisionTestPlayerScript>(CollisionTestPlayerScript.ScriptId, "Collision Test Player");
        Register<TriggerZoneScript>(TriggerZoneScript.ScriptId, "Trigger Zone");
        Register<ImpactReporterScript>(ImpactReporterScript.ScriptId, "Impact Reporter");
        Register<LaunchPadScript>(LaunchPadScript.ScriptId, "Launch Pad");
        Register<BallDispenserScript>(BallDispenserScript.ScriptId, "Ball Dispenser");
        Register<GateLeverScript>(GateLeverScript.ScriptId, "Gate Lever");
        Register<ColorCycleScript>(ColorCycleScript.ScriptId, "Color Cycle");
        Register<WeatherTestScript>(WeatherTestScript.ScriptId, "Weather Test");
        Register<PersistentPlayerScript>(PersistentPlayerScript.ScriptId, "Persistent Player");
        Register<ScenePortalScript>(ScenePortalScript.ScriptId, "Scene Portal");
        Register<CompanionOrbScript>(CompanionOrbScript.ScriptId, "Companion Orb");
        Register<PauseMenuScript>(PauseMenuScript.ScriptId, "Pause Menu");
        Register<RagdollTestScript>(RagdollTestScript.ScriptId, "Ragdoll Test");
    }

    public static IReadOnlyCollection<ScriptDefinition> All => Definitions.Values;

    public static void Register<T>(string id, string displayName) where T : ScriptBehaviour, new()
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Script id is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("Script name is required.", nameof(displayName));
        Definitions.Add(id, new ScriptDefinition(id, displayName, () => new T(), typeof(T)));
    }

    public static ScriptDefinition Find(string id) =>
        id != null && Definitions.TryGetValue(id, out var definition) ? definition : null;
}
