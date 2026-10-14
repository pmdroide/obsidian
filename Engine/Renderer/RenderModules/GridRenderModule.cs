using Engine.Entities;
using Engine.Recources;
using Engine.Renderer.Helper;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Renderer.RenderModules;

/// <summary>
/// Editor ground grid: an infinite plane at <see cref="GameSettings.e_gridheight"/> (Z is up), drawn as one
/// full-screen pass over the final image, under the gizmos. Geometry in front of it hides it through the
/// linear G-buffer depth. Shown in edit mode while <see cref="GameSettings.e_showgrid"/> is on (Anvil's
/// viewport Grid button); never in Play or the standalone game.
/// </summary>
public sealed class GridRenderModule : IDisposable
{
    private static readonly Vector3 LineColor = new Vector3(0.82f, 0.82f, 0.82f);
    private static readonly Vector3 AxisXColor = new Vector3(0.90f, 0.30f, 0.30f);
    private static readonly Vector3 AxisYColor = new Vector3(0.45f, 0.80f, 0.30f);

    private readonly Effect _shader;

    public GridRenderModule(ContentManager content) =>
        _shader = content.Load<Effect>("Shaders/Editor/Grid");

    /// <summary>Whether the grid is drawn this frame: edit mode with the grid switched on.</summary>
    public static bool IsVisible => GameSettings.e_enableeditor && GameSettings.e_showgrid;

    /// <param name="staticViewProjection">Unjittered view projection, so the lines don't shimmer with TAA.</param>
    /// <param name="depthMap">Linear G-buffer depth (view Z / -farClip).</param>
    public void Draw(GraphicsDevice graphics, Camera camera, Matrix view, Matrix staticViewProjection,
        Texture2D depthMap, float farClip, FullScreenTriangle fullScreenTriangle)
    {
        if (!IsVisible || camera == null || fullScreenTriangle == null) return;

        Set("InverseViewProjection", Matrix.Invert(staticViewProjection));
        Set("View", view);
        Set("CameraPosition", camera.Position);
        Set("GridHeight", Finite(GameSettings.e_gridheight, 0));
        Set("CellSize", Math.Max(0.01f, Finite(GameSettings.e_gridcellsize, 1)));
        Set("FadeDistance", Math.Max(1f, Finite(GameSettings.e_gridfadedistance, 250)));
        Set("Opacity", 1f);
        Set("LineColor", LineColor);
        Set("AxisXColor", AxisXColor);
        Set("AxisYColor", AxisYColor);
        bool hasDepth = depthMap != null && farClip > 0;
        _shader.Parameters["HasDepth"]?.SetValue(hasDepth);
        _shader.Parameters["DepthMap"]?.SetValue(hasDepth ? depthMap : null);
        Set("FarClip", farClip);

        graphics.DepthStencilState = DepthStencilState.None;
        graphics.RasterizerState = RasterizerState.CullNone;
        graphics.BlendState = BlendState.NonPremultiplied;
        _shader.CurrentTechnique = _shader.Techniques["Grid"];
        _shader.CurrentTechnique.Passes[0].Apply();
        fullScreenTriangle.Draw(graphics);

        _shader.Parameters["DepthMap"]?.SetValue((Texture2D)null);
        graphics.DepthStencilState = DepthStencilState.Default;
        graphics.RasterizerState = RasterizerState.CullCounterClockwise;
        graphics.BlendState = BlendState.Opaque;
    }

    private static float Finite(float value, float fallback) => float.IsFinite(value) ? value : fallback;

    // The compiler strips unused parameters; a missing one must not break the frame.
    private void Set(string name, Matrix value) => _shader.Parameters[name]?.SetValue(value);
    private void Set(string name, Vector3 value) => _shader.Parameters[name]?.SetValue(value);
    private void Set(string name, float value) => _shader.Parameters[name]?.SetValue(value);

    public void Dispose() => _shader.Dispose();
}
