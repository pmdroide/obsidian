using Engine.Entities;
using Engine.Recources;
using Engine.Renderer.Helper;
using Engine.Renderer.RenderModules.Default;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using DirectionalLight = Engine.Entities.DirectionalLight;

namespace Engine.Renderer.RenderModules;

public sealed class WaterRenderModule : IRenderModule, IDisposable
{
    private readonly Effect _shader;
    public WaterRenderModule(ContentManager content) =>
        _shader = content.Load<Effect>("Shaders/Forward/Water");

    public void Draw(GraphicsDevice graphics, MeshMaterialLibrary meshes, Matrix viewProjection,
        Camera camera, TextureCube environment, List<DirectionalLight> lights, GameTime time)
    {
        _shader.Parameters["CameraPositionWS"].SetValue(camera.Position);
        _shader.Parameters["Time"].SetValue((float)time.TotalGameTime.TotalSeconds);
        _shader.Parameters["HasEnvironment"].SetValue(environment != null);
        _shader.Parameters["EnvironmentMap"].SetValue(environment);
        var light = lights.FirstOrDefault(l => l.IsEnabled && l.Intensity > 0);
        _shader.Parameters["LightDirection"].SetValue(light == null ? Vector3.UnitZ : -light.Direction);
        _shader.Parameters["LightColor"].SetValue(light == null ? Vector3.Zero : light.ColorV3 * light.Intensity * 0.1f);
        // Sort water instances even when CPU culling/sorting is disabled.
        for (int i = 0; i < meshes.Index; i++)
        {
            var batch = meshes.MaterialLib[i];
            if (batch.GetMaterial().Type != MaterialEffect.MaterialTypes.Water || batch.Index == 0) continue;
            batch.DistanceSquared = Vector3.DistanceSquared(camera.Position,
                batch.GetMeshLibrary()[0].GetWorldMatrices()[0].World.Translation);
        }
        meshes.Draw(MeshMaterialLibrary.RenderType.Water, viewProjection, renderModule: this);
        graphics.DepthStencilState = DepthStencilState.Default;
        graphics.RasterizerState = RasterizerState.CullCounterClockwise;
        graphics.BlendState = BlendState.Opaque;
    }

    public void SetMaterialSettings(MaterialEffect material)
    {
        _shader.Parameters["SurfaceColor"].SetValue(material.DiffuseColor);
        _shader.Parameters["Roughness"].SetValue(material.Roughness);
        _shader.Parameters["Opacity"].SetValue(material.Opacity);
        _shader.Parameters["WaveScale"].SetValue(material.WaveScale);
        _shader.Parameters["WaveSpeed"].SetValue(material.WaveSpeed);
        _shader.Parameters["WaveStrength"].SetValue(material.WaveStrength);
    }

    public void Apply(Matrix world, Matrix? view, Matrix viewProjection)
    {
        _shader.Parameters["World"].SetValue(world);
        _shader.Parameters["WorldViewProj"].SetValue(world * viewProjection);
        _shader.Parameters["WorldInverseTranspose"].SetValue(Matrix.Transpose(Matrix.Invert(world)));
        _shader.CurrentTechnique.Passes[0].Apply();
    }

    public void Dispose() => _shader.Dispose();
}
