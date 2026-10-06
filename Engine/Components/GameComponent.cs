using Engine.Entities;
using Microsoft.Xna.Framework;
using System.Text.Json.Serialization;

namespace Engine.Components;

/// <summary>Persisted behaviour attached to a gameobject. Runtime state stays out of JSON.</summary>
public abstract class GameComponent
{
    /// <summary>Identifies this attachment across snapshots and scene saves, independent of its type.</summary>
    [JsonIgnore] public Guid InstanceId { get; internal set; } = Guid.NewGuid();
    public bool Enabled { get; set; } = true;
    public virtual void OnStart(BasicEntity owner) { }
    public virtual void OnUpdate(BasicEntity owner, GameTime time) { }
    /// <summary>Called after the editor (or scene code) changes this component's settings.</summary>
    public virtual void OnChanged(BasicEntity owner)
    {
        if (!Enabled || !owner.IsEnabled) OnStop();
    }
    /// <summary>Called right after <see cref="BasicEntity.AddComponent"/> attaches it.</summary>
    public virtual void OnAdded(BasicEntity owner) => OnChanged(owner);
    /// <summary>Called right after <see cref="BasicEntity.RemoveComponent"/> detaches it.</summary>
    public virtual void OnRemoved(BasicEntity owner) => OnStop();
    public virtual void OnStop() { }
}
