using Engine.Recources;
using Microsoft.Xna.Framework;

namespace Engine.Scripting
{
    /// <summary>
    /// Test behaviour that turns its owning entity into a looping 3D audio emitter while
    /// in <see cref="GameMode.Play"/>. Attach in code (scripts are code-only in v1). The
    /// emitter follows the entity's live position via the position-source overload, so the
    /// <see cref="AudioManager"/> updates it every frame until the channel stops.
    /// </summary>
    public sealed class AudioTestScript : IScript
    {
        public void OnStart(IScriptContext context)
        {
            Audio.PlaySound3D("loop3d", () => context.Owner.Position, volume: 1f, loop: true);
        }

        public void OnUpdate(IScriptContext context, GameTime gameTime)
        {
            // Emitter position is reconciled centrally in AudioManager.UpdateListener.
        }
    }
}
