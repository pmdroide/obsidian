using System;
using Engine.Renderer.Helper;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Graphics.PackedVector;

namespace Engine.Renderer.Lighting
{
    /// <summary>
    /// Image based lighting the way Source 2 / every PBR engine does it (Docs/Research/Source2Look phase 3.1):
    /// <list type="bullet">
    /// <item>GGX prefiltering: a captured cubemap becomes a cube whose mip m holds the reflection of roughness
    /// m / (mips - 1), rendered per face and mip by Shaders/Deferred/EnvironmentPrefilter.fx and copied into a
    /// <see cref="TextureCube"/> (MonoGame can't render into one mip of a cube target).</item>
    /// <item>The split-sum BRDF lookup texture (F0 scale, bias by NdotV and roughness), integrated once on the CPU.</item>
    /// </list>
    /// </summary>
    public sealed class EnvironmentPrefilter : IDisposable
    {
        public const int LutSize = 32;

        private readonly Effect _effect;
        private Texture2D _brdfLut;
        private RenderTarget2D[] _levelTargets = Array.Empty<RenderTarget2D>();

        public EnvironmentPrefilter(ContentManager content)
        {
            _effect = content.Load<Effect>("Shaders/Deferred/EnvironmentPrefilter");
        }

        /// <summary>The split-sum lookup: x = NdotV, y = roughness; r = F0 scale, g = bias.</summary>
        public Texture2D BrdfLut(GraphicsDevice graphics)
        {
            if (_brdfLut != null) return _brdfLut;
            Vector2[] data = IntegrateBrdf(LutSize, 256);
            var packed = new HalfVector2[data.Length];
            for (int i = 0; i < data.Length; i++) packed[i] = new HalfVector2(data[i]);
            _brdfLut = new Texture2D(graphics, LutSize, LutSize, false, SurfaceFormat.HalfVector2);
            _brdfLut.SetData(packed);
            return _brdfLut;
        }

        /// <summary>Mip levels of a prefiltered cube of <paramref name="size"/> texels: down to 4 x 4 (roughness 1).</summary>
        public static int MipCount(int size)
        {
            int count = 1;
            while (size > 4) { size /= 2; count++; }
            return count;
        }

        /// <summary>
        /// Prefilters <paramref name="source"/> (a cube with mips) into a cube of <paramref name="size"/> texels, reusing
        /// <paramref name="target"/> when it has that size. Returns the prefiltered cube.
        /// </summary>
        public TextureCube Prefilter(GraphicsDevice graphics, FullScreenTriangle triangle, TextureCube source, int size,
            TextureCube target)
        {
            int mips = MipCount(size);
            if (target == null || target.Size != size || target.LevelCount != mips)
            {
                target?.Dispose();
                target = new TextureCube(graphics, size, true, SurfaceFormat.HalfVector4);
            }
            if (_levelTargets.Length < mips || _levelTargets[0]?.Width != size)
            {
                foreach (RenderTarget2D rt in _levelTargets) rt?.Dispose();
                _levelTargets = new RenderTarget2D[mips];
                for (int m = 0; m < mips; m++)
                    _levelTargets[m] = new RenderTarget2D(graphics, Math.Max(size >> m, 1), Math.Max(size >> m, 1), false,
                        SurfaceFormat.HalfVector4, DepthFormat.None, 0, RenderTargetUsage.DiscardContents);
            }

            RenderTargetBinding[] previousTargets = graphics.GetRenderTargets();
            BlendState blend = graphics.BlendState;
            DepthStencilState depth = graphics.DepthStencilState;
            RasterizerState rasterizer = graphics.RasterizerState;
            graphics.BlendState = BlendState.Opaque;
            graphics.DepthStencilState = DepthStencilState.None;
            graphics.RasterizerState = RasterizerState.CullNone;

            _effect.Parameters["SourceCube"].SetValue(source);
            _effect.Parameters["SourceSize"]?.SetValue((float)source.Size);
            _effect.Parameters["SourceMaxMip"]?.SetValue((float)Math.Max(source.LevelCount - 1, 0));
            EffectPass pass = _effect.Techniques["Prefilter"].Passes[0];

            for (int m = 0; m < mips; m++)
            {
                RenderTarget2D rt = _levelTargets[m];
                float roughness = mips > 1 ? m / (float)(mips - 1) : 0;
                _effect.Parameters["Roughness"]?.SetValue(roughness);
                //More samples where the lobe is wide, the texels few
                _effect.Parameters["SampleCount"]?.SetValue(m == 0 ? 1 : m < 3 ? 48 : 96);
                var texels = new HalfVector4[rt.Width * rt.Height];
                for (int face = 0; face < 6; face++)
                {
                    graphics.SetRenderTarget(rt);
                    _effect.Parameters["Face"]?.SetValue(face);
                    pass.Apply();
                    triangle.Draw(graphics);
                    graphics.SetRenderTarget(null);
                    rt.GetData(texels);
                    target.SetData((CubeMapFace)face, m, null, texels, 0, texels.Length);
                }
            }

            _effect.Parameters["SourceCube"].SetValue((TextureCube)null);
            graphics.SetRenderTargets(previousTargets);
            graphics.BlendState = blend;
            graphics.DepthStencilState = depth;
            graphics.RasterizerState = rasterizer;
            return target;
        }

        /// <summary>
        /// Split-sum BRDF integral (Karis 2013): for NdotV = (x + 0.5) / size and roughness = (y + 0.5) / size, the scale
        /// and bias of F0 that GGX (Smith-Schlick, k = alpha / 2 for image based light) reflects over the hemisphere.
        /// </summary>
        public static Vector2[] IntegrateBrdf(int size, int samples)
        {
            var result = new Vector2[size * size];
            for (int y = 0; y < size; y++)
            {
                float roughness = (y + 0.5f) / size;
                float alpha = roughness * roughness;
                float k = alpha / 2;
                for (int x = 0; x < size; x++)
                {
                    float ndotv = (x + 0.5f) / size;
                    var v = new Vector3(MathF.Sqrt(1 - ndotv * ndotv), 0, ndotv);
                    float a = 0, b = 0;
                    for (int i = 0; i < samples; i++)
                    {
                        float u1 = (i + 0.5f) / samples;
                        float u2 = RadicalInverse((uint)i);
                        float phi = 2 * MathF.PI * u1;
                        float cosTheta = MathF.Sqrt((1 - u2) / (1 + (alpha * alpha - 1) * u2));
                        float sinTheta = MathF.Sqrt(1 - cosTheta * cosTheta);
                        var h = new Vector3(sinTheta * MathF.Cos(phi), sinTheta * MathF.Sin(phi), cosTheta);
                        Vector3 l = 2 * Vector3.Dot(v, h) * h - v;
                        float ndotl = MathHelper.Clamp(l.Z, 0, 1);
                        float ndoth = MathHelper.Clamp(h.Z, 0, 1);
                        float vdoth = MathHelper.Clamp(Vector3.Dot(v, h), 0, 1);
                        if (ndotl <= 0) continue;
                        float gv = ndotv / (ndotv * (1 - k) + k);
                        float gl = ndotl / (ndotl * (1 - k) + k);
                        float gVis = gv * gl * vdoth / (ndoth * ndotv);
                        float fc = MathF.Pow(1 - vdoth, 5);
                        a += (1 - fc) * gVis;
                        b += fc * gVis;
                    }
                    result[y * size + x] = new Vector2(a / samples, b / samples);
                }
            }
            return result;
        }

        private static float RadicalInverse(uint bits)
        {
            bits = (bits << 16) | (bits >> 16);
            bits = ((bits & 0x55555555u) << 1) | ((bits & 0xAAAAAAAAu) >> 1);
            bits = ((bits & 0x33333333u) << 2) | ((bits & 0xCCCCCCCCu) >> 2);
            bits = ((bits & 0x0F0F0F0Fu) << 4) | ((bits & 0xF0F0F0F0u) >> 4);
            bits = ((bits & 0x00FF00FFu) << 8) | ((bits & 0xFF00FF00u) >> 8);
            return bits * 2.3283064365386963e-10f;
        }

        public void Dispose()
        {
            _effect?.Dispose();
            _brdfLut?.Dispose();
            foreach (RenderTarget2D rt in _levelTargets) rt?.Dispose();
        }
    }
}
