using System;
using Engine.Recources;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Voxels
{
    /// <summary>
    /// How one surface is shaded: roughness, material type, how strongly its pixels are embossed, its glow, and
    /// how it reacts to rain (soaks darker, fills puddles on top faces).
    /// </summary>
    public readonly record struct VoxelSurfaceStyle(float Roughness, MaterialEffect.MaterialTypes Type, float Bump,
        float Glow = 0, bool RainWet = true, bool Puddles = true)
    {
        // Every non-glowing surface uses the Wet Surface type: rain soaks it, and its ambient light uses the
        // view-dependent fresnel of wet pixels, so all blocks are lit alike. Leaves and snow don't pool water.
        public static VoxelSurfaceStyle For(VoxelSurface surface) => surface switch
        {
            VoxelSurface.GrassTop => new(0.85f, MaterialEffect.MaterialTypes.WetSurface, 1.6f),
            VoxelSurface.GrassSide => new(0.88f, MaterialEffect.MaterialTypes.WetSurface, 1.6f),
            VoxelSurface.Dirt => new(0.95f, MaterialEffect.MaterialTypes.WetSurface, 1.8f),
            VoxelSurface.Stone => new(0.72f, MaterialEffect.MaterialTypes.WetSurface, 2.4f),
            VoxelSurface.Sand => new(0.9f, MaterialEffect.MaterialTypes.WetSurface, 1.0f),
            VoxelSurface.Snow => new(0.55f, MaterialEffect.MaterialTypes.WetSurface, 0.8f, RainWet: false, Puddles: false),
            VoxelSurface.Bark => new(0.85f, MaterialEffect.MaterialTypes.WetSurface, 2.6f),
            VoxelSurface.LogTop => new(0.8f, MaterialEffect.MaterialTypes.WetSurface, 1.4f),
            VoxelSurface.Leaves => new(0.75f, MaterialEffect.MaterialTypes.WetSurface, 1.1f, Puddles: false),
            VoxelSurface.Planks => new(0.7f, MaterialEffect.MaterialTypes.WetSurface, 2.4f),
            VoxelSurface.Brick => new(0.85f, MaterialEffect.MaterialTypes.WetSurface, 3.0f),
            // Emissive: the compose pass multiplies the albedo by the metallic channel x 8.
            VoxelSurface.Lamp => new(0.4f, MaterialEffect.MaterialTypes.Emissive, 1.2f, Glow: 3.2f, RainWet: false, Puddles: false),
            _ => new(0.8f, MaterialEffect.MaterialTypes.WetSurface, 1),
        };
    }

    /// <summary>
    /// Paints each <see cref="VoxelSurface"/>'s textures in code: a 16 x 16 pixel-art pattern scaled up to
    /// <see cref="Size"/>² so it stays crisp up close, plus a normal map embossing the pattern's height.
    /// Deterministic: the same surface always gets the same pixels. <see cref="Paint"/> needs no GPU.
    /// </summary>
    public static class VoxelTextures
    {
        public const int Size = 64;
        public const int Grid = 16;
        private const int Scale = Size / Grid;

        /// <summary>Fills sRGB albedo and tangent-space normal pixels (<see cref="Size"/>² each, row-major).</summary>
        public static void Paint(VoxelSurface surface, Color[] albedo, Color[] normal)
        {
            var noise = new VoxelNoise(9000 + (int)surface);
            var colors = new Vector3[Grid * Grid];
            var heights = new float[Grid * Grid];
            for (int y = 0; y < Grid; y++)
            for (int x = 0; x < Grid; x++)
            {
                PaintPixel(surface, noise, x, y, out Vector3 color, out float height);
                colors[x + y * Grid] = color;
                heights[x + y * Grid] = height;
            }

            float bump = VoxelSurfaceStyle.For(surface).Bump;
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                int gx = x / Scale, gy = y / Scale;
                // A faint texel-level grain so large flat areas don't look like a solid fill.
                float grain = 1 + (noise.Random01(x, y, 99) - 0.5f) * 0.05f;
                Vector3 c = Vector3.Clamp(colors[gx + gy * Grid] * grain, Vector3.Zero, Vector3.One);
                albedo[x + y * Size] = new Color(c);

                float H(int tx, int ty) => heights[(((tx % Size) + Size) % Size) / Scale + (((ty % Size) + Size) % Size) / Scale * Grid];
                float dx = H(x + 1, y) - H(x - 1, y);
                float dy = H(x, y + 1) - H(x, y - 1);
                Vector3 n = Vector3.Normalize(new Vector3(-dx * bump, -dy * bump, 1));
                normal[x + y * Size] = new Color(n * 0.5f + new Vector3(0.5f));
            }
        }

        /// <summary>Uploads a surface's albedo and normal map with full mip chains.</summary>
        public static void Create(GraphicsDevice graphics, VoxelSurface surface, out Texture2D albedo, out Texture2D normal)
        {
            var albedoPixels = new Color[Size * Size];
            var normalPixels = new Color[Size * Size];
            Paint(surface, albedoPixels, normalPixels);
            albedo = Upload(graphics, albedoPixels, false);
            normal = Upload(graphics, normalPixels, true);
        }

        private static Texture2D Upload(GraphicsDevice graphics, Color[] pixels, bool isNormalMap)
        {
            var texture = new Texture2D(graphics, Size, Size, true, SurfaceFormat.Color);
            Color[] level = pixels;
            int size = Size;
            for (int mip = 0; mip < texture.LevelCount; mip++)
            {
                texture.SetData(mip, null, level, 0, size * size);
                if (size == 1) break;
                level = Downsample(level, size, isNormalMap);
                size /= 2;
            }
            return texture;
        }

        private static Color[] Downsample(Color[] source, int size, bool isNormalMap)
        {
            int half = size / 2;
            var result = new Color[half * half];
            for (int y = 0; y < half; y++)
            for (int x = 0; x < half; x++)
            {
                Vector4 sum = source[x * 2 + y * 2 * size].ToVector4() + source[x * 2 + 1 + y * 2 * size].ToVector4() +
                              source[x * 2 + (y * 2 + 1) * size].ToVector4() + source[x * 2 + 1 + (y * 2 + 1) * size].ToVector4();
                sum /= 4;
                if (isNormalMap)
                {
                    Vector3 n = new Vector3(sum.X, sum.Y, sum.Z) * 2 - Vector3.One;
                    n = n.LengthSquared() > 1e-6f ? Vector3.Normalize(n) : Vector3.UnitZ;
                    sum = new Vector4(n * 0.5f + new Vector3(0.5f), 1);
                }
                result[x + y * half] = new Color(sum);
            }
            return result;
        }

        private static Vector3 Rgb(int r, int g, int b) => new Vector3(r, g, b) / 255f;

        /// <summary>Smooth value noise (0..1) on a 4 x 4 lattice that wraps with the 16-pixel tile, so tiles join seamlessly.</summary>
        private static float TileNoise(VoxelNoise noise, int x, int y, int salt)
        {
            const int Cells = 4, CellPixels = Grid / Cells;
            float gx = (x + 0.5f) / CellPixels, gy = (y + 0.5f) / CellPixels;
            int i0 = (int)MathF.Floor(gx), j0 = (int)MathF.Floor(gy);
            float fx = gx - i0, fy = gy - j0;
            fx = fx * fx * (3 - 2 * fx);
            fy = fy * fy * (3 - 2 * fy);
            float V(int i, int j) => noise.Random01(((i % Cells) + Cells) % Cells, ((j % Cells) + Cells) % Cells, 500 + salt);
            float top = MathHelper.Lerp(V(i0, j0), V(i0 + 1, j0), fx);
            float bottom = MathHelper.Lerp(V(i0, j0 + 1), V(i0 + 1, j0 + 1), fx);
            return MathHelper.Lerp(top, bottom, fy);
        }

        // One 16 x 16 pattern pixel: sRGB colour (0..1) and a height (0 = recessed .. 1 = raised).
        private static void PaintPixel(VoxelSurface surface, VoxelNoise noise, int x, int y, out Vector3 color, out float height)
        {
            float r = noise.Random01(x, y);
            // Soft blotches four pixels across, wrapping with the tile.
            float blotch = TileNoise(noise, x, y, 1);
            switch (surface)
            {
                case VoxelSurface.GrassTop:
                    color = Vector3.Lerp(Rgb(54, 98, 36), Rgb(98, 150, 60), r * 0.7f + blotch * 0.3f);
                    if (r > 0.93f) color = Rgb(124, 176, 76);
                    height = r * 0.6f;
                    break;

                case VoxelSurface.GrassSide:
                {
                    int fringe = 3 + (noise.Random01(x, 0, 5) < 0.5f ? 1 : 0) + (noise.Random01(x, 0, 6) > 0.8f ? 2 : 0);
                    if (y < fringe)
                    {
                        color = Vector3.Lerp(Rgb(66, 112, 42), Rgb(112, 166, 70), r);
                        height = 0.6f + r * 0.4f;
                    }
                    else
                    {
                        Dirt(noise, x, y, r, out color, out height);
                        if (y == fringe) color *= 0.85f;
                    }
                    break;
                }

                case VoxelSurface.Dirt:
                    Dirt(noise, x, y, r, out color, out height);
                    break;

                case VoxelSurface.Stone:
                {
                    color = Vector3.Lerp(Rgb(104, 104, 110), Rgb(148, 148, 152), blotch * 0.7f + r * 0.3f);
                    height = 0.4f + blotch * 0.6f;
                    // Cracks: thin lines where a second noise field crosses zero.
                    float crack = MathF.Abs(TileNoise(noise, x + 2, y + 1, 2) - 0.5f);
                    if (crack < 0.035f)
                    {
                        color *= 0.62f;
                        height = 0;
                    }
                    break;
                }

                case VoxelSurface.Sand:
                    color = Vector3.Lerp(Rgb(208, 188, 134), Rgb(232, 214, 160), r * 0.6f + blotch * 0.4f);
                    if (r < 0.06f) color = Rgb(186, 164, 116);
                    height = r * 0.3f;
                    break;

                case VoxelSurface.Snow:
                    color = Vector3.Lerp(Rgb(222, 230, 242), Rgb(246, 249, 253), r * 0.5f + blotch * 0.5f);
                    height = blotch * 0.4f;
                    break;

                case VoxelSurface.Bark:
                {
                    float column = noise.Random01(x, 0, 21);
                    color = Vector3.Lerp(Rgb(78, 56, 36), Rgb(116, 86, 56), column * 0.7f + r * 0.3f);
                    height = 0.5f + column * 0.5f;
                    // Deep vertical grooves on some columns, broken up along the trunk.
                    if (column < 0.25f && noise.Random01(x, y / 3, 22) < 0.8f)
                    {
                        color *= 0.7f;
                        height = 0;
                    }
                    break;
                }

                case VoxelSurface.LogTop:
                {
                    float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(Grid / 2f));
                    if (distance > 7.2f)
                    {
                        color = Vector3.Lerp(Rgb(78, 56, 36), Rgb(104, 76, 48), r);
                        height = 0.8f;
                    }
                    else
                    {
                        bool ring = (int)(distance * 0.8f + noise.Random01(x, y, 3) * 0.6f) % 2 == 0;
                        color = ring ? Rgb(184, 146, 96) : Rgb(156, 120, 76);
                        color *= 0.95f + r * 0.1f;
                        height = ring ? 0.6f : 0.4f;
                    }
                    break;
                }

                case VoxelSurface.Leaves:
                    color = Vector3.Lerp(Rgb(40, 84, 34), Rgb(80, 136, 58), r * 0.6f + blotch * 0.4f);
                    height = 0.5f + r * 0.5f;
                    if (r < 0.16f)
                    {
                        color = Rgb(24, 50, 22);
                        height = 0;
                    }
                    break;

                case VoxelSurface.Planks:
                {
                    int board = y / 4;
                    float tone = noise.Random01(board, 0, 31);
                    color = Vector3.Lerp(Rgb(150, 112, 68), Rgb(190, 148, 94), tone * 0.6f + r * 0.25f);
                    height = 0.8f;
                    // Grain: a few darker streaks along the board.
                    if (noise.Random01(x / 3, y, 32) < 0.18f) color *= 0.88f;
                    int joint = (int)(noise.Random01(board, 0, 33) * Grid);
                    if (y % 4 == 3 || x == joint)
                    {
                        color *= 0.6f;
                        height = 0;
                    }
                    break;
                }

                case VoxelSurface.Brick:
                {
                    int row = y / 4;
                    int shifted = x + (row % 2) * 4;
                    int brick = shifted / 8;
                    bool mortar = y % 4 == 3 || shifted % 8 == 0;
                    if (mortar)
                    {
                        color = Vector3.Lerp(Rgb(176, 170, 158), Rgb(198, 192, 180), r);
                        height = 0;
                    }
                    else
                    {
                        float tone = noise.Random01(brick, row, 41);
                        color = Vector3.Lerp(Rgb(138, 58, 44), Rgb(178, 84, 62), tone * 0.7f + r * 0.3f);
                        height = 0.85f + r * 0.15f;
                    }
                    break;
                }

                case VoxelSurface.Lamp:
                {
                    bool frame = x == 0 || y == 0 || x == Grid - 1 || y == Grid - 1;
                    bool bars = x == Grid / 2 || y == Grid / 2;
                    if (frame)
                    {
                        color = Rgb(70, 56, 38);
                        height = 1;
                    }
                    else if (bars)
                    {
                        color = Rgb(236, 176, 96);
                        height = 0.7f;
                    }
                    else
                    {
                        color = Vector3.Lerp(Rgb(255, 214, 140), Rgb(255, 236, 190), blotch);
                        height = 0.4f;
                    }
                    break;
                }

                default:
                    color = new Vector3(0.5f);
                    height = 0.5f;
                    break;
            }
        }

        private static void Dirt(VoxelNoise noise, int x, int y, float r, out Vector3 color, out float height)
        {
            float blotch = TileNoise(noise, x, y, 3);
            color = Vector3.Lerp(Rgb(98, 68, 44), Rgb(140, 100, 68), r * 0.6f + blotch * 0.4f);
            height = 0.3f + r * 0.4f;
            if (r > 0.94f)
            {
                // Pebbles.
                color = new Vector3(142, 126, 106) / 255f;
                height = 1;
            }
        }
    }
}
