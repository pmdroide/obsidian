using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Physics
{
    /// <summary>
    /// The built-in looks of broken glass, painted once per graphics device (no content files): the
    /// <see cref="Damaged"/> crazing the cracked panels show (Half-Life: Alyx's <c>shatterglass_damaged</c>
    /// material) and the <see cref="Edge"/> fracture surface of the broken edges. Both tile in U and V and are
    /// linear (the shaders don't linearise texels); alpha multiplies the material's opacity, so lines are drawn as
    /// more opaque glass. Game thread only.
    /// </summary>
    public static class GlassTextures
    {
        /// <summary>Alpha of clear glass in <see cref="Damaged"/>; the damaged material divides its opacity by it.</summary>
        public const float DamagedClearAlpha = 0.62f;
        /// <summary>Metres one tile of <see cref="Damaged"/> covers on the pane.</summary>
        public const float DamagedTileSize = 0.5f;

        private const int DamagedSize = 256;
        private const int EdgeSize = 128;

        private static GraphicsDevice _device;
        private static Texture2D _damaged, _edge;

        /// <summary>Fine crazing, a few scratches and a faint frost.</summary>
        public static Texture2D Damaged(GraphicsDevice device)
        {
            Prepare(device);
            return _damaged ??= Paint(device, DamagedSize, PaintDamaged);
        }

        /// <summary>Hackle bands across the glass's thickness, the rippled surface of a fresh break.</summary>
        public static Texture2D Edge(GraphicsDevice device)
        {
            Prepare(device);
            return _edge ??= Paint(device, EdgeSize, PaintEdge);
        }

        private static void Prepare(GraphicsDevice device)
        {
            if (_device == device && (_damaged == null || !_damaged.IsDisposed) && (_edge == null || !_edge.IsDisposed)) return;
            _damaged = null;
            _edge = null;
            _device = device;
        }

        private static Texture2D Paint(GraphicsDevice device, int size, Func<float, float, Color> pixel)
        {
            var data = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    data[y * size + x] = pixel((x + 0.5f) / size, (y + 0.5f) / size);
            var texture = new Texture2D(device, size, size, true, SurfaceFormat.Color);
            texture.SetData(data);
            // Mip levels by box filtering, so the fine lines fade with distance instead of shimmering.
            Color[] level = data;
            for (int mip = 1, w = size / 2; w >= 1; mip++, w /= 2)
            {
                var next = new Color[w * w];
                for (int y = 0; y < w; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int s = w * 2;
                        Vector4 sum = level[2 * y * s + 2 * x].ToVector4() + level[2 * y * s + 2 * x + 1].ToVector4()
                            + level[(2 * y + 1) * s + 2 * x].ToVector4() + level[(2 * y + 1) * s + 2 * x + 1].ToVector4();
                        next[y * w + x] = new Color(sum * 0.25f);
                    }
                texture.SetData(mip, null, next, 0, next.Length);
                level = next;
            }
            return texture;
        }

        private static Color PaintDamaged(float u, float v)
        {
            // Crazing: borders of a wrapped Voronoi of 9 x 9 jittered cells, shown only where a slow noise says so.
            const int cells = 9;
            float f1 = float.MaxValue, f2 = float.MaxValue;
            int cx = (int)(u * cells), cy = (int)(v * cells);
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int gx = cx + dx, gy = cy + dy;
                    Vector2 site = new Vector2(gx + Hash(Wrap(gx, cells), Wrap(gy, cells), 1), gy + Hash(Wrap(gx, cells), Wrap(gy, cells), 2)) / cells;
                    float d = Vector2.Distance(site, new Vector2(u, v));
                    if (d < f1) { f2 = f1; f1 = d; }
                    else if (d < f2) f2 = d;
                }
            float crack = 1 - Smooth(0.0015f, 0.006f, f2 - f1);
            crack *= Smooth(0.42f, 0.62f, Noise(u, v, 4, 3));

            // Scratches: thin wrapped lines at random angles.
            float scratch = 0;
            for (int i = 0; i < 9; i++)
            {
                var a = new Vector2(Hash(i, 0, 5), Hash(i, 0, 6));
                float angle = Hash(i, 0, 7) * MathHelper.Pi, length = 0.08f + Hash(i, 0, 8) * 0.25f;
                var dir = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                for (int oy = -1; oy <= 1; oy++)
                    for (int ox = -1; ox <= 1; ox++)
                    {
                        Vector2 p = new Vector2(u + ox, v + oy) - a;
                        float t = Math.Clamp(Vector2.Dot(p, dir), 0, length);
                        float d = Vector2.Distance(p, dir * t);
                        scratch = Math.Max(scratch, (1 - Smooth(0.0006f, 0.0025f, d)) * (0.15f + 0.25f * Hash(i, 0, 9)));
                    }
            }

            float frost = Noise(u, v, 16, 11);
            float line = Math.Max(crack, scratch);
            float alpha = DamagedClearAlpha + (1 - DamagedClearAlpha) * line + 0.06f * (frost - 0.5f);
            float rgb = 0.93f + 0.07f * line;
            return new Color(new Vector4(rgb, rgb, rgb, Math.Clamp(alpha, 0, 1)));
        }

        private static Color PaintEdge(float u, float v)
        {
            // U runs along the edge, V across the glass: bands of hackle across the thickness, wavering slightly.
            float warp = (Noise(u, v, 6, 21) - 0.5f) * 0.05f;
            float bands = 0.5f + 0.5f * MathF.Sin((u + warp) * MathHelper.TwoPi * 37);
            float fine = Noise(u, v, 24, 23);
            float ripple = 0.5f + 0.5f * MathF.Sin((u * 11 + Noise(u, v, 3, 25)) * MathHelper.TwoPi);
            float shade = 0.72f + 0.16f * bands + 0.08f * fine + 0.04f * ripple;
            float alpha = 0.82f + 0.12f * bands + 0.06f * fine;
            return new Color(new Vector4(shade, shade, shade, Math.Clamp(alpha, 0, 1)));
        }

        // Wrapped value noise (period `cells` in U and V).
        private static float Noise(float u, float v, int cells, int seed)
        {
            float x = u * cells, y = v * cells;
            int x0 = (int)MathF.Floor(x), y0 = (int)MathF.Floor(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3 - 2 * fx);
            fy = fy * fy * (3 - 2 * fy);
            float a = Hash(Wrap(x0, cells), Wrap(y0, cells), seed), b = Hash(Wrap(x0 + 1, cells), Wrap(y0, cells), seed);
            float c = Hash(Wrap(x0, cells), Wrap(y0 + 1, cells), seed), d = Hash(Wrap(x0 + 1, cells), Wrap(y0 + 1, cells), seed);
            return MathHelper.Lerp(MathHelper.Lerp(a, b, fx), MathHelper.Lerp(c, d, fx), fy);
        }

        private static int Wrap(int i, int n) => ((i % n) + n) % n;

        private static float Smooth(float edge0, float edge1, float x)
        {
            float t = Math.Clamp((x - edge0) / (edge1 - edge0), 0, 1);
            return t * t * (3 - 2 * t);
        }

        // Deterministic 0..1 per integer triple.
        private static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                // Each input is mixed in turn (lowbias32), so nearby integers give unrelated values.
                uint h = Mix((uint)seed * 0x9E3779B9u);
                h = Mix(h ^ (uint)x);
                h = Mix(h ^ (uint)y);
                return (h >> 8) / (float)0x1000000;
            }
        }

        private static uint Mix(uint h)
        {
            unchecked
            {
                h ^= h >> 16;
                h *= 0x7FEB352Du;
                h ^= h >> 15;
                h *= 0x846CA68Bu;
                h ^= h >> 16;
                return h;
            }
        }
    }
}
