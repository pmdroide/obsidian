using Microsoft.Xna.Framework;

namespace Engine.Renderer.RenderModules.Default
{
    public interface IRenderModule
    {
        void Apply(Matrix localWorldMatrix, Matrix? view, Matrix viewProjection);
    }
}