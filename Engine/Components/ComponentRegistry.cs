using System.Text.Json;
using Engine.Entities;

namespace Engine.Components;

public sealed class ComponentRecord
{
    public string Type { get; set; }
    public Guid InstanceId { get; set; }
    public JsonElement Data { get; set; }
}

/// <param name="Id">Stable id written to scene files and used by the editor bridge. Never rename it.</param>
/// <param name="CreateFor">Builds the default component for a gameobject (e.g. Material copies the model's surface).</param>
public sealed record ComponentDefinition(string Id, string DisplayName, Type ComponentType,
    Func<BasicEntity, GameComponent> CreateFor, bool AllowMultiple = false)
{
    public GameComponent Create(BasicEntity owner = null) => CreateFor(owner);
}

/// <summary>Register each new component once here; the bridge and scene format are generic.</summary>
public static class ComponentRegistry
{
    private static readonly Dictionary<string, ComponentDefinition> Definitions = new();
    private static readonly Dictionary<Type, ComponentDefinition> ByType = new();

    static ComponentRegistry()
    {
        Register<MaterialComponent>(MaterialComponent.TypeId, "Material", MaterialComponent.FromOwner);
        Register<PhysicsComponent>(PhysicsComponent.TypeId, "Physics");
        Register<AudioComponent>(AudioComponent.TypeId, "Audio");
        Register<ScriptBehaviourComponent>(ScriptBehaviourComponent.TypeId, "Script Behaviour", allowMultiple: true);
    }

    /// <summary>Definitions in registration order (the Add Component menu order).</summary>
    public static IReadOnlyCollection<ComponentDefinition> All => Definitions.Values;

    public static void Register<T>(string id, string displayName, Func<BasicEntity, T> createFor = null, bool allowMultiple = false)
        where T : GameComponent, new()
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Component id is required.", nameof(id));
        if (Definitions.ContainsKey(id)) throw new ArgumentException($"Component id '{id}' is already registered.");
        if (ByType.ContainsKey(typeof(T)))
            throw new ArgumentException($"Component {typeof(T).Name} is already registered.");
        var definition = new ComponentDefinition(id, displayName, typeof(T),
            owner => createFor != null ? createFor(owner) : new T(), allowMultiple);
        Definitions.Add(id, definition);
        ByType.Add(typeof(T), definition);
    }

    public static ComponentDefinition Find(string id) =>
        id != null && Definitions.TryGetValue(id, out var definition) ? definition : null;

    public static ComponentDefinition Find(Type type) =>
        type != null && ByType.TryGetValue(type, out var definition) ? definition : null;

    public static ComponentRecord Capture(GameComponent component)
    {
        var definition = Find(component.GetType())
            ?? throw new InvalidOperationException($"Component {component.GetType().Name} is not registered.");
        return new ComponentRecord
        {
            Type = definition.Id,
            InstanceId = component.InstanceId,
            Data = JsonSerializer.SerializeToElement(component, definition.ComponentType),
        };
    }

    public static GameComponent Restore(ComponentRecord record)
    {
        var definition = Find(record?.Type);
        if (definition == null) return null;
        var component = (GameComponent)record.Data.Deserialize(definition.ComponentType);
        if (component != null && record.InstanceId != Guid.Empty) component.InstanceId = record.InstanceId;
        return component;
    }

    public static GameComponent Copy(GameComponent component)
    {
        var copy = Restore(Capture(component));
        copy.InstanceId = Guid.NewGuid();
        return copy;
    }
}
