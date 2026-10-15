using Microsoft.Xna.Framework;

namespace Engine.Renderer.RenderModules.Default
{
    public interface IRenderModule
    {
        void Apply(Matrix localWorldMatrix, Matrix? view, Matrix viewProjection);
    }

    /// <summary>
    /// A module that writes motion vectors (the G-buffer, for TAA): MeshMaterialLibrary hands it each instance's world
    /// matrix of the previous frame right before <see cref="IRenderModule.Apply"/>.
    /// </summary>
    public interface IVelocityModule
    {
        void SetPreviousWorld(Matrix previousWorld);
    }
}