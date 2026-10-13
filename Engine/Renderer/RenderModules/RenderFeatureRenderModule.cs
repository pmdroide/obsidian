using System.Collections.Generic;
using Engine.Editor;
using Engine.Renderer.Features;
using Engine.Renderer.Helper;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Renderer.RenderModules;

/// <summary>
/// Draws the user render features (<see cref="RenderFeatures"/>) of one injection point over the image so far.
/// Each feature is a full-screen pass that reads the previous result as <c>ScreenTexture</c> and writes a
/// scratch target of the same size and format (ping-pong); the last result is copied back into the pipeline's
/// target, or handed on when the caller can't take it in place. The engine fills the shader parameters it
/// declares (see <see cref="Context"/> and the template in <see cref="RenderFeatureShaderTemplate"/>).
/// </summary>
public sealed class RenderFeatureRenderModule : IDisposable
{
    /// <summary>Per-frame values the renderer passes to every feature.</summary>
    public struct Context
    {
        public Matrix View;
        public Matrix Projection;
        public Matrix ViewProjection;
        public Matrix InverseViewProjection;
        public Vector3 CameraPosition;
        public float FarClip;
        public Texture2D DepthMap;
        public Texture2D NormalMap;
        public Texture2D AlbedoMap;
        public float Time;
        public float DeltaTime;
    }

    // Blended features change the colour only: the image keeps its alpha.
    private static readonly BlendState AlphaBlend = ColorOnly("RenderFeature.AlphaBlend", Blend.SourceAlpha, Blend.InverseSourceAlpha);
    private static readonly BlendState AdditiveBlend = ColorOnly("RenderFeature.Additive", Blend.SourceAlpha, Blend.One);
    private static readonly BlendState MultiplyBlend = ColorOnly("RenderFeature.Multiply", Blend.DestinationColor, Blend.Zero);

    private static BlendState ColorOnly(string name, Blend source, Blend destination) => new BlendState
    {
        Name = name,
        ColorSourceBlend = source,
        ColorDestinationBlend = destination,
        AlphaSourceBlend = Blend.Zero,
        AlphaDestinationBlend = Blend.One,
    };

    // Two scratch targets per surface format (HDR before post processing, LDR after).
    private readonly Dictionary<SurfaceFormat, RenderTarget2D[]> _scratch = new Dictionary<SurfaceFormat, RenderTarget2D[]>();
    private SpriteBatch _spriteBatch;

    public void Initialize(GraphicsDevice graphics) => _spriteBatch = new SpriteBatch(graphics);

    /// <summary>True when a feature of this injection point draws this frame.</summary>
    public static bool HasActive(RenderFeatureEvent at)
    {
        IReadOnlyList<RenderFeature> features = RenderFeatures.All;
        for (int i = 0; i < features.Count; i++)
            if (features[i].Event == at && features[i].IsActive) return true;
        return false;
    }

    /// <summary>
    /// Runs the active features of <paramref name="at"/> over <paramref name="image"/>. Returns the image to go on
    /// with: <paramref name="image"/> itself when <paramref name="inPlace"/> (the result is copied into it), else
    /// the scratch target holding the result (valid until the next injection point of the same format).
    /// </summary>
    public RenderTarget2D Draw(GraphicsDevice graphics, RenderFeatureEvent at, RenderTarget2D image, in Context context,
        FullScreenTriangle fullScreenTriangle, bool inPlace)
    {
        if (image == null || !HasActive(at)) return image;

        RenderTarget2D source = image;
        int next = 0;
        IReadOnlyList<RenderFeature> features = RenderFeatures.All;
        for (int i = 0; i < features.Count; i++)
        {
            RenderFeature feature = features[i];
            if (feature.Event != at || !feature.IsActive) continue;

            RenderTarget2D destination = Scratch(graphics, image, next);
            graphics.SetRenderTarget(destination);
            // Blended features draw over a copy of the image.
            if (feature.Blend != RenderFeatureBlend.Replace) Copy(source, destination);
            if (DrawFeature(graphics, feature, source, destination, context, fullScreenTriangle))
            {
                source = destination;
                next ^= 1;
            }
        }

        graphics.BlendState = BlendState.Opaque;
        graphics.DepthStencilState = DepthStencilState.Default;
        graphics.RasterizerState = RasterizerState.CullCounterClockwise;

        if (source == image || !inPlace) return source;
        graphics.SetRenderTarget(image);
        Copy(source, image);
        return image;
    }

    private static bool DrawFeature(GraphicsDevice graphics, RenderFeature feature, Texture2D source, RenderTarget2D destination,
        in Context context, FullScreenTriangle fullScreenTriangle)
    {
        Effect effect = feature.Effect;
        try
        {
            Set(feature, "ScreenTexture", source);
            Set(feature, "DepthMap", context.DepthMap);
            Set(feature, "NormalMap", context.NormalMap);
            Set(feature, "AlbedoMap", context.AlbedoMap);
            Set(feature, "Resolution", new Vector2(destination.Width, destination.Height));
            Set(feature, "InverseResolution", new Vector2(1f / destination.Width, 1f / destination.Height));
            Set(feature, "Time", context.Time);
            Set(feature, "DeltaTime", context.DeltaTime);
            Set(feature, "Intensity", feature.Intensity);
            Set(feature, "Color", ToVector4(feature.Color));
            Set(feature, "Params", ToVector4(feature.Params));
            Set(feature, "CameraPosition", context.CameraPosition);
            Set(feature, "FarClip", context.FarClip);
            Set(feature, "View", context.View);
            Set(feature, "Projection", context.Projection);
            Set(feature, "ViewProjection", context.ViewProjection);
            Set(feature, "InverseViewProjection", context.InverseViewProjection);

            graphics.BlendState = feature.Blend switch
            {
                RenderFeatureBlend.AlphaBlend => AlphaBlend,
                RenderFeatureBlend.Additive => AdditiveBlend,
                RenderFeatureBlend.Multiply => MultiplyBlend,
                _ => BlendState.Opaque,
            };
            graphics.DepthStencilState = DepthStencilState.None;
            graphics.RasterizerState = RasterizerState.CullNone;

            foreach (EffectPass pass in effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                fullScreenTriangle.Draw(graphics);
            }
            Set(feature, "ScreenTexture", null);
            return true;
        }
        catch (Exception e)
        {
            // Usually a parameter declared with another type than the engine sets (e.g. a texture named Time).
            // Stop drawing it until it is recompiled instead of throwing every frame.
            EditorBridge.Log($"RenderFeatures: '{feature.Name}' failed to draw: {e.Message}");
            RenderFeatures.ReportDrawError(feature, "Draw failed: " + e.Message);
            return false;
        }
    }

    private static Vector4 ToVector4(float[] v) =>
        v != null && v.Length >= 4 ? new Vector4(v[0], v[1], v[2], v[3]) : Vector4.One;

    // The compiler strips unused parameters; a missing one is skipped.
    private static EffectParameter Parameter(RenderFeature feature, string name)
    {
        if (!feature.ParameterCache.TryGetValue(name, out EffectParameter parameter))
        {
            parameter = feature.Effect.Parameters[name];
            feature.ParameterCache[name] = parameter;
        }
        return parameter;
    }

    private static void Set(RenderFeature feature, string name, Texture2D value) => Parameter(feature, name)?.SetValue(value);
    private static void Set(RenderFeature feature, string name, float value) => Parameter(feature, name)?.SetValue(value);
    private static void Set(RenderFeature feature, string name, Vector2 value) => Parameter(feature, name)?.SetValue(value);
    private static void Set(RenderFeature feature, string name, Vector3 value) => Parameter(feature, name)?.SetValue(value);
    private static void Set(RenderFeature feature, string name, Vector4 value) => Parameter(feature, name)?.SetValue(value);
    private static void Set(RenderFeature feature, string name, Matrix value) => Parameter(feature, name)?.SetValue(value);

    // Into the render target already set (setting a DiscardContents target again would clear it).
    private void Copy(Texture2D source, RenderTarget2D destination)
    {
        _spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone);
        _spriteBatch.Draw(source, new Rectangle(0, 0, destination.Width, destination.Height), Color.White);
        _spriteBatch.End();
    }

    private RenderTarget2D Scratch(GraphicsDevice graphics, RenderTarget2D like, int index)
    {
        if (!_scratch.TryGetValue(like.Format, out RenderTarget2D[] pair))
            _scratch[like.Format] = pair = new RenderTarget2D[2];
        RenderTarget2D target = pair[index];
        if (target == null || target.IsDisposed || target.Width != like.Width || target.Height != like.Height)
        {
            target?.Dispose();
            pair[index] = target = new RenderTarget2D(graphics, like.Width, like.Height, false, like.Format,
                DepthFormat.None, 0, RenderTargetUsage.DiscardContents);
        }
        return target;
    }

    public void Dispose()
    {
        foreach (RenderTarget2D[] pair in _scratch.Values)
            foreach (RenderTarget2D target in pair) target?.Dispose();
        _scratch.Clear();
        _spriteBatch?.Dispose();
    }
}
