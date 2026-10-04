using System.Text.Json;

namespace Engine.Components;

public sealed class ComponentRecord
{
    public string Type { get; set; }
    public JsonElement Data { get; set; }
}

public sealed record ComponentDefinition(string Id, string DisplayName, Type ComponentType)
{
    public GameComponent Create() => (GameComponent)Activator.CreateInstance(ComponentType);
}

/// <summary>Register each new component once here; the bridge and scene format are generic.</summary>
public static class ComponentRegistry
{
    private static readonly Dictionary<string, ComponentDefinition> Definitions = new();

    static ComponentRegistry()
    {
        Register<AudioComponent>(AudioComponent.TypeId, "Audio");
        Register<MaterialComponent>(MaterialComponent.TypeId, "Material");
    }

    public static IReadOnlyCollection<ComponentDefinition> All => Definitions.Values;

    public static void Register<T>(string id, string displayName) where T : GameComponent, new()
    {
        if (Definitions.Values.Any(d => d.ComponentType == typeof(T)))
            throw new ArgumentException($"Component {typeof(T).Name} is already registered.");
        Definitions.Add(id, new ComponentDefinition(id, displayName, typeof(T)));
    }

    public static ComponentDefinition Find(string id) =>
        id != null && Definitions.TryGetValue(id, out var definition) ? definition : null;

    public static ComponentRecord Capture(GameComponent component)
    {
        var definition = Definitions.Values.Single(d => d.ComponentType == component.GetType());
        return new ComponentRecord
        {
            Type = definition.Id,
            Data = JsonSerializer.SerializeToElement(component, definition.ComponentType),
        };
    }

    public static GameComponent Restore(ComponentRecord record)
    {
        var definition = Find(record.Type);
        return definition == null ? null :
            (GameComponent)record.Data.Deserialize(definition.ComponentType);
    }

    public static GameComponent Copy(GameComponent component) => Restore(Capture(component));
}
