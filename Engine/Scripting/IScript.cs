using Engine.Entities;
using Engine.Logic;
using Microsoft.Xna.Framework;

namespace Engine.Scripting
{
    /// <summary>
    /// Minimal behaviour hook attached to a <see cref="BasicEntity"/>. Scripts only
    /// tick in <see cref="GameMode.Play"/>; entering Play calls <see cref="OnStart"/>
    /// once, then <see cref="OnUpdate"/> every frame until <see cref="GameMode.Edit"/>.
    /// Not serialised in v1 of the <c>.obsc</c> format — script attachment is via
    /// code only for now.
    /// </summary>
    public interface IScript
    {
        void OnStart(IScriptContext context);
        void OnUpdate(IScriptContext context, GameTime gameTime);
    }

    public interface IScriptContext
    {
        BasicEntity Owner { get; }
        Scene Scene { get; }
    }

    internal sealed class ScriptContext : IScriptContext
    {
        public BasicEntity Owner { get; set; }
        public Scene Scene { get; set; }
    }
}
