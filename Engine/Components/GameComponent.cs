using Engine.Entities;
using Microsoft.Xna.Framework;

namespace Engine.Components;

/// <summary>Persisted behaviour attached to a gameobject. Runtime state stays out of JSON.</summary>
public abstract class GameComponent
{
    public bool Enabled { get; set; } = true;
    public virtual void OnStart(BasicEntity owner) { }
    public virtual void OnUpdate(BasicEntity owner, GameTime time) { }
    public virtual void OnChanged(BasicEntity owner)
    {
        if (!Enabled || !owner.IsEnabled) OnStop();
    }
    public virtual void OnStop() { }
}
