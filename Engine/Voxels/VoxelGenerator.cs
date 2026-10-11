using System;
using System.Threading;
using System.Threading.Tasks;

namespace Engine.Voxels
{
    /// <summary>
    /// Builds a <see cref="VoxelWorld"/> from <see cref="VoxelWorldSettings"/>: a height field per terrain type,
    /// soil layers (grass over dirt over stone, sand at the waterline, snow on high peaks), spaghetti caves and
    /// trees. Pure CPU work with no shared state, so it runs on a worker thread; the same settings always build
    /// the same world.
    /// </summary>
    public static class VoxelGenerator
    {
        /// <summary>Peaks above this fraction of the world height get snow (Mountains).</summary>
        public const float SnowLine = 0.7f;

        public static VoxelWorld Generate(VoxelWorldSettings settings, CancellationToken cancel = default)
        {
            var world = new VoxelWorld(settings);
            var noise = new VoxelNoise(settings.Seed);
            int sx = world.SizeX, sy = world.SizeY, sz = world.SizeZ;
            int sea = world.SeaLevel;

            // 1. Surface height of every column, in blocks.
            // Rows are independent (the noise is read-only), so both column passes run in parallel.
            var options = new ParallelOptions { CancellationToken = cancel };
            var heights = new int[sx * sy];
            Parallel.For(0, sy, options, y =>
            {
                for (int x = 0; x < sx; x++)
                    heights[x + y * sx] = Math.Clamp((int)MathF.Floor(Height(settings, noise, x, y, sx, sy, sz, sea)), 1, sz - 2);
            });

            // 2. Columns: stone, soil, the top block; caves carved below the surface.
            float snowLine = sz * SnowLine;
            Parallel.For(0, sy, options, y =>
            {
                for (int x = 0; x < sx; x++)
                {
                    int top = heights[x + y * sx];
                    int steepness = Steepness(heights, x, y, sx, sy);
                    VoxelBlock surface, soil;
                    int soilDepth;
                    if (sea > 0 && top < sea + 2)
                    {
                        surface = soil = VoxelBlock.Sand;
                        soilDepth = 3;
                    }
                    else if (settings.Terrain == VoxelTerrain.Mountains && top > snowLine - noise.Random01(x, y, 7) * 4)
                    {
                        surface = VoxelBlock.Snow;
                        soil = VoxelBlock.Stone;
                        soilDepth = 1;
                    }
                    else if (steepness >= 4 || (settings.Terrain == VoxelTerrain.Mountains && steepness >= 3))
                    {
                        surface = soil = VoxelBlock.Stone;
                        soilDepth = 0;
                    }
                    else
                    {
                        surface = VoxelBlock.Grass;
                        soil = VoxelBlock.Dirt;
                        soilDepth = 3 + (int)(noise.Random01(x, y, 3) * 2);
                    }

                    for (int z = 0; z <= top; z++)
                    {
                        VoxelBlock block = z == top ? surface : z >= top - soilDepth ? soil : VoxelBlock.Stone;
                        if (settings.Caves && z >= 2 && z < top - 3 && IsCave(noise, x, y, z)) block = VoxelBlock.Air;
                        world.SetGenerated(x, y, z, block);
                    }
                }
            });

            // 3. Trees on grass above the water.
            if (settings.Trees > 0) PlantTrees(world, noise, heights, settings.Trees, cancel);
            return world;
        }

        /// <summary>Surface height (blocks) of one column for the settings' terrain type.</summary>
        public static float Height(VoxelWorldSettings settings, VoxelNoise noise, int x, int y, int sx, int sy, int sz, int sea)
        {
            switch (settings.Terrain)
            {
                case VoxelTerrain.Flat:
                    return Math.Max(sea + 3, sz * 0.35f);

                case VoxelTerrain.Mountains:
                {
                    float baseHeight = sz * 0.3f + noise.Fbm(x / 96f, y / 96f, 3) * sz * 0.1f;
                    float ridges = noise.Ridged(x / 72f + 100, y / 72f + 100, 5);
                    // Some areas are valleys, some ranges.
                    float range = Smooth(0.0f, 0.45f, noise.Fbm(x / 140f - 50, y / 140f + 20, 2) + 0.25f);
                    return baseHeight + ridges * ridges * sz * 0.62f * range + noise.Fbm(x / 10f, y / 10f, 2) * 1.5f;
                }

                case VoxelTerrain.Islands:
                {
                    float waterline = sea > 0 ? sea : sz * 0.3f;
                    float nx = (x + 0.5f) / sx * 2 - 1, ny = (y + 0.5f) / sy * 2 - 1;
                    float edge = MathF.Sqrt(nx * nx + ny * ny);
                    // Above 0 is land: it climbs quickly from narrow beaches; below 0 a shallow sea floor.
                    float land = noise.Fbm(x / 40f, y / 40f, 4) * 0.85f + 0.22f - edge * 0.75f;
                    float hills = Math.Max(0, land) * (noise.Fbm(x / 18f + 40, y / 18f, 2) * 0.6f + 0.4f);
                    return land > 0 ? waterline + 1 + (land + hills) * sz * 0.6f : waterline + land * sz * 0.3f;
                }

                default: // Hills
                {
                    float rolling = noise.Fbm(x / 56f, y / 56f, 4) * sz * 0.2f;
                    float detail = noise.Fbm(x / 14f + 20, y / 14f - 20, 2) * 2.2f;
                    // Low enough that the default sea (0.3) fills the deepest valleys as lakes.
                    return sz * 0.36f + rolling + detail;
                }
            }
        }

        private static float Smooth(float a, float b, float v)
        {
            float t = Math.Clamp((v - a) / (b - a), 0, 1);
            return t * t * (3 - 2 * t);
        }

        // Largest height step to a neighbouring column: cliffs show bare stone.
        private static int Steepness(int[] heights, int x, int y, int sx, int sy)
        {
            int h = heights[x + y * sx], steep = 0;
            if (x > 0) steep = Math.Max(steep, Math.Abs(heights[x - 1 + y * sx] - h));
            if (x < sx - 1) steep = Math.Max(steep, Math.Abs(heights[x + 1 + y * sx] - h));
            if (y > 0) steep = Math.Max(steep, Math.Abs(heights[x + (y - 1) * sx] - h));
            if (y < sy - 1) steep = Math.Max(steep, Math.Abs(heights[x + (y + 1) * sx] - h));
            return steep;
        }

        /// <summary>Spaghetti caves: tunnels where two 3D noise fields both cross zero.</summary>
        public static bool IsCave(VoxelNoise noise, int x, int y, int z)
        {
            float a = noise.Noise(x / 22f, y / 22f, z / 14f);
            if (MathF.Abs(a) > 0.09f) return false;
            float b = noise.Noise(x / 22f + 71.3f, y / 22f - 33.1f, z / 14f + 12.7f);
            return MathF.Abs(b) < 0.09f;
        }

        private static void PlantTrees(VoxelWorld world, VoxelNoise noise, int[] heights, float density, CancellationToken cancel)
        {
            int sx = world.SizeX, sy = world.SizeY, sz = world.SizeZ;
            // At density 1 about one tree per 70 grass columns; trunks keep 4 blocks apart.
            float chance = density * 0.014f;
            var taken = new bool[sx * sy];
            for (int y = 3; y < sy - 3; y++)
            {
                cancel.ThrowIfCancellationRequested();
                for (int x = 3; x < sx - 3; x++)
                {
                    int top = heights[x + y * sx];
                    if (top + 8 >= sz || world.GetBlock(x, y, top) != VoxelBlock.Grass) continue;
                    if (noise.Random01(x, y, 11) >= chance || taken[x + y * sx]) continue;

                    int trunk = 4 + (int)(noise.Random01(x, y, 13) * 3);
                    for (int z = top + 1; z <= top + trunk; z++) world.SetGenerated(x, y, z, VoxelBlock.Wood);
                    world.SetGenerated(x, y, top, VoxelBlock.Dirt);

                    // A rounded crown around the trunk's top.
                    int crown = top + trunk;
                    for (int dz = -2; dz <= 1; dz++)
                    {
                        int radius = dz >= 1 ? 1 : 2;
                        for (int dy = -radius; dy <= radius; dy++)
                        for (int dx = -radius; dx <= radius; dx++)
                        {
                            int lx = x + dx, ly = y + dy, lz = crown + dz;
                            bool corner = Math.Abs(dx) == radius && Math.Abs(dy) == radius;
                            if (corner && (radius == 1 || noise.Random01(lx, ly, lz) < 0.6f)) continue;
                            if (!world.InBounds(lx, ly, lz) || world.GetBlock(lx, ly, lz) != VoxelBlock.Air) continue;
                            world.SetGenerated(lx, ly, lz, VoxelBlock.Leaves);
                        }
                    }
                    if (world.InBounds(x, y, crown + 2)) world.SetGenerated(x, y, crown + 2, VoxelBlock.Leaves);

                    for (int dy = -4; dy <= 4; dy++)
                    for (int dx = -4; dx <= 4; dx++)
                    {
                        int tx = x + dx, ty = y + dy;
                        if ((uint)tx < (uint)sx && (uint)ty < (uint)sy) taken[tx + ty * sx] = true;
                    }
                }
            }
        }
    }
}
