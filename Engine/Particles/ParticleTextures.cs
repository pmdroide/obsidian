using Engine.Editor;
using Engine.Recources;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Particles;

/// <summary>
/// The images particles draw with. The built-in ones are painted on first use (white with an alpha shape, so the
/// particle colour tints them; Smoke adds a little shading); Custom loads a content texture by path.
/// </summary>
public sealed class ParticleTextures : IDisposable
{
    private const int Size = 128;
    // The flipbook: 4 x 4 frames of a smoke puff growing and thinning out.
    public const int SheetTiles = 4;

    private readonly GraphicsDevice _graphics;
    private readonly Dictionary<ParticleTexture, Texture2D> _builtIn = new();
    private readonly Dictionary<string, Texture2D> _custom = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _failed = new(StringComparer.OrdinalIgnoreCase);

    public ParticleTextures(GraphicsDevice graphics) => _graphics = graphics;

    /// <summary>The texture of a renderer module; a Custom path that doesn't load falls back to Soft Circle.</summary>
    public Texture2D For(ParticleRendererModule renderer)
    {
        if (renderer.Texture == ParticleTexture.Custom)
        {
            Texture2D custom = LoadCustom(renderer.TexturePath);
            if (custom != null) return custom;
            return BuiltIn(ParticleTexture.SoftCircle);
        }
        return BuiltIn(renderer.Texture);
    }

    public Texture2D BuiltIn(ParticleTexture kind)
    {
        if (_builtIn.TryGetValue(kind, out Texture2D texture)) return texture;
        texture = kind == ParticleTexture.SmokeSheet ? Sheet() : Paint(kind);
        _builtIn[kind] = texture;
        return texture;
    }

    private Texture2D LoadCustom(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        path = path.Trim().Replace('\\', '/');
        if (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
            path = path[..^4];
        if (_custom.TryGetValue(path, out Texture2D texture)) return texture;
        if (_failed.Contains(path) || Globals.content == null) return null;
        try
        {
            texture = Globals.content.Load<Texture2D>(path);
            _custom[path] = texture;
            return texture;
        }
        catch (Exception ex)
        {
            _failed.Add(path);
            EditorBridge.Log($"Particles: texture '{path}' didn't load ({ex.Message}); drawing Soft Circle.");
            return null;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Painting
    // ---------------------------------------------------------------------------------------------

    private Texture2D Paint(ParticleTexture kind)
    {
        var pixels = new Color[Size * Size];
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                // -1..1 across, +Y up (row 0 is the top of the quad).
                float u = (x + 0.5f) / Size * 2 - 1;
                float v = 1 - (y + 0.5f) / Size * 2;
                pixels[y * Size + x] = Pixel(kind, u, v);
            }
        return Upload(pixels, Size, Size);
    }

    private static Color Pixel(ParticleTexture kind, float u, float v)
    {
        float r2 = u * u + v * v;
        float r = (float)Math.Sqrt(r2);
        switch (kind)
        {
            case ParticleTexture.Glow:
            {
                float a = (float)Math.Exp(-r2 * 9) * 0.75f + (float)Math.Exp(-r2 * 40) * 0.5f;
                return White(a * Edge(r));
            }
            case ParticleTexture.Spark:
            {
                // Thin along U, long along V (the direction of travel for stretched billboards).
                float a = (float)Math.Exp(-u * u * 28) * (float)Math.Pow(Math.Max(0, 1 - Math.Abs(v)), 1.4);
                return White(Math.Min(1, a * 1.3f));
            }
            case ParticleTexture.Smoke:
                return Smoke(u, v, 0, 0, 1, 1);
            case ParticleTexture.Flame:
            {
                // A teardrop: round at the bottom, pointed at the top, flickering edges.
                float height = (v + 1) * 0.5f;                       // 0 bottom .. 1 top
                float width = 0.55f * (float)Math.Pow(Math.Max(0, 1 - height), 0.7) * (0.6f + 0.4f * (float)Math.Sqrt(Math.Max(0, height * 2.2f)));
                float wobble = Fbm(u * 3 + 11, v * 2.2f, 3) * 0.18f;
                float across = Math.Abs(u + wobble * height) / Math.Max(width, 0.001f);
                float a = Smooth(1, 0.55f, across) * Smooth(0, 0.12f, height) * Smooth(1, 0.8f, height);
                float core = Smooth(0.7f, 0, across) * Smooth(0.85f, 0.2f, height);
                return new Color(new Vector4(1, 1, 1, Math.Clamp(a * (0.75f + 0.25f * core), 0, 1)));
            }
            case ParticleTexture.Star:
            {
                float arms = (float)(Math.Exp(-Math.Abs(u) * 26) * Math.Exp(-Math.Abs(v) * 3.2) +
                                     Math.Exp(-Math.Abs(v) * 26) * Math.Exp(-Math.Abs(u) * 3.2));
                float a = arms * 0.9f + (float)Math.Exp(-r2 * 30) * 0.8f + (float)Math.Exp(-r2 * 6) * 0.2f;
                return White(Math.Min(1, a) * Edge(r));
            }
            case ParticleTexture.Ring:
            {
                float d = (r - 0.72f) / 0.12f;
                return White((float)Math.Exp(-d * d) * Edge(r));
            }
            case ParticleTexture.Square:
            {
                float edge = Math.Max(Math.Abs(u), Math.Abs(v));
                return White(Smooth(1, 0.97f, edge));
            }
            default: // SoftCircle
            {
                float a = Math.Max(0, 1 - r2);
                return White(a * a);
            }
        }
    }

    private Texture2D Sheet()
    {
        int frameSize = Size;
        int size = frameSize * SheetTiles;
        var pixels = new Color[size * size];
        for (int frame = 0; frame < SheetTiles * SheetTiles; frame++)
        {
            int ox = frame % SheetTiles * frameSize, oy = frame / SheetTiles * frameSize;
            float t = frame / (float)(SheetTiles * SheetTiles - 1);
            for (int y = 0; y < frameSize; y++)
                for (int x = 0; x < frameSize; x++)
                {
                    float u = (x + 0.5f) / frameSize * 2 - 1;
                    float v = 1 - (y + 0.5f) / frameSize * 2;
                    // Grows from a dense knot to a wide, thin wisp; the pattern rolls upwards.
                    float scale = MathHelper.Lerp(1.6f, 1.0f, t);
                    pixels[(oy + y) * size + ox + x] = Smoke(u * scale, v * scale, t * 1.7f, t * 3.1f,
                        MathHelper.Lerp(1.15f, 0.35f, t * t), MathHelper.Lerp(0.45f, 0.95f, t));
                }
        }
        return Upload(pixels, size, size);
    }

    private static Color Smoke(float u, float v, float offsetX, float offsetY, float density, float softness)
    {
        float r = (float)Math.Sqrt(u * u + v * v);
        float noise = Fbm(u * 2.3f + 3.7f + offsetX, v * 2.3f - 1.3f - offsetY, 5);
        float shape = Smooth(1, 0.15f + 0.3f * (1 - softness), r + (noise - 0.5f) * 0.55f);
        float a = Math.Clamp(shape * (0.55f + noise * 0.9f) * density, 0, 1);
        // Lighter on the upper left, a little darker inside: reads as volume even unlit.
        float shade = Math.Clamp(0.78f + (v - u) * 0.1f + noise * 0.22f, 0, 1);
        return new Color(new Vector4(shade, shade, shade, a));
    }

    private static Color White(float alpha) => new(new Vector4(1, 1, 1, Math.Clamp(alpha, 0, 1)));

    // Forces round shapes to 0 at the quad's inscribed circle, so no corners show.
    private static float Edge(float r) => Smooth(1, 0.85f, r);

    /// <summary>Smoothstep from <paramref name="edge0"/> (0) to <paramref name="edge1"/> (1); edges may run backwards.</summary>
    private static float Smooth(float edge0, float edge1, float x)
    {
        float t = Math.Clamp((x - edge0) / (edge1 - edge0), 0, 1);
        return t * t * (3 - 2 * t);
    }

    private static float Fbm(float x, float y, int octaves)
    {
        float sum = 0, amplitude = 0.5f, total = 0;
        for (int i = 0; i < octaves; i++)
        {
            sum += Noise(x, y) * amplitude;
            total += amplitude;
            x = x * 2.03f + 17.1f;
            y = y * 2.03f - 7.7f;
            amplitude *= 0.5f;
        }
        return sum / total;
    }

    private static float Noise(float x, float y)
    {
        int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y);
        float xf = x - xi, yf = y - yi;
        float u = xf * xf * (3 - 2 * xf), v = yf * yf * (3 - 2 * yf);
        return MathHelper.Lerp(MathHelper.Lerp(Hash(xi, yi), Hash(xi + 1, yi), u),
            MathHelper.Lerp(Hash(xi, yi + 1), Hash(xi + 1, yi + 1), u), v);
    }

    private static float Hash(int x, int y)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263);
            h = (h ^ (h >> 13)) * 1274126177;
            return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
        }
    }

    /// <summary>Uploads with a full mip chain (2x2 averages, alpha-weighted so colour doesn't bleed from clear pixels).</summary>
    private Texture2D Upload(Color[] pixels, int width, int height)
    {
        var texture = new Texture2D(_graphics, width, height, true, SurfaceFormat.Color);
        texture.SetData(0, null, pixels, 0, pixels.Length);
        Color[] level = pixels;
        int w = width, h = height;
        for (int mip = 1; mip < texture.LevelCount; mip++)
        {
            int nw = Math.Max(1, w / 2), nh = Math.Max(1, h / 2);
            var next = new Color[nw * nh];
            for (int y = 0; y < nh; y++)
                for (int x = 0; x < nw; x++)
                {
                    Vector4 sum = Vector4.Zero;
                    float alpha = 0;
                    for (int dy = 0; dy < 2; dy++)
                        for (int dx = 0; dx < 2; dx++)
                        {
                            Vector4 c = level[Math.Min(y * 2 + dy, h - 1) * w + Math.Min(x * 2 + dx, w - 1)].ToVector4();
                            sum += new Vector4(c.X * c.W, c.Y * c.W, c.Z * c.W, c.W);
                            alpha += c.W;
                        }
                    next[y * nw + x] = alpha > 0
                        ? new Color(new Vector4(sum.X / alpha, sum.Y / alpha, sum.Z / alpha, alpha / 4))
                        : new Color(255, 255, 255, 0);
                }
            texture.SetData(mip, null, next, 0, next.Length);
            level = next;
            w = nw;
            h = nh;
        }
        return texture;
    }

    public void Dispose()
    {
        foreach (Texture2D texture in _builtIn.Values) texture.Dispose();
        _builtIn.Clear();
        // Custom textures belong to the content manager.
        _custom.Clear();
    }
}
