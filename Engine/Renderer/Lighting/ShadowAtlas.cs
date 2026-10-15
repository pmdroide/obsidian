using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Renderer.Lighting
{
    /// <summary>
    /// One shadow texture shared by every point and spot light (Source 2's CShadowAtlas, Docs/Research/Source2Look
    /// phase 2.2). Each cube face a light needs is a square tile, power-of-two sized, handed out by a quadtree (buddy)
    /// allocator; tiles stay with their light between frames, so unchanged lights keep their shadows without redrawing.
    /// Stores 1 - distance / radius (ShadowMap.fx DrawDistanceDepth), cleared to 0 (far, lit).
    /// </summary>
    public sealed class ShadowAtlas : IDisposable
    {
        public const int MinTileSize = 64;

        public int Size { get; }
        public RenderTarget2D Target { get; private set; }

        private readonly int _levels;
        //Free tiles per level (level 0 = the whole atlas, each level halves the size), by top-left texel
        private readonly HashSet<Point>[] _free;

        public ShadowAtlas(GraphicsDevice graphics, int size)
        {
            Size = Math.Max(MinTileSize, NextPowerOfTwo(size));
            _levels = Log2(Size / MinTileSize) + 1;
            _free = new HashSet<Point>[_levels];
            for (int i = 0; i < _levels; i++) _free[i] = new HashSet<Point>();
            _free[0].Add(Point.Zero);
            if (graphics != null)
                Target = new RenderTarget2D(graphics, Size, Size, false, SurfaceFormat.HalfSingle, DepthFormat.Depth24,
                    0, RenderTargetUsage.PreserveContents);
        }

        /// <summary>Texels free right now (for stats and checks).</summary>
        public long FreeTexels
        {
            get
            {
                long total = 0;
                for (int level = 0; level < _levels; level++)
                {
                    long side = Size >> level;
                    total += _free[level].Count * side * side;
                }
                return total;
            }
        }

        /// <summary>Takes a free square tile of <paramref name="size"/> texels (rounded up to a power of two).</summary>
        public bool TryAllocate(int size, out Point origin)
        {
            origin = Point.Zero;
            size = Math.Clamp(NextPowerOfTwo(size), MinTileSize, Size);
            int level = Log2(Size / size);

            //The smallest free tile that is big enough, split down to the wanted size
            int from = level;
            while (from >= 0 && _free[from].Count == 0) from--;
            if (from < 0) return false;

            Point tile = First(_free[from]);
            _free[from].Remove(tile);
            for (int l = from; l < level; l++)
            {
                int half = (Size >> l) / 2;
                _free[l + 1].Add(new Point(tile.X + half, tile.Y));
                _free[l + 1].Add(new Point(tile.X, tile.Y + half));
                _free[l + 1].Add(new Point(tile.X + half, tile.Y + half));
            }
            origin = tile;
            return true;
        }

        /// <summary>Gives a tile back; merges it with its three siblings when they are all free.</summary>
        public void Free(Point origin, int size)
        {
            size = Math.Clamp(NextPowerOfTwo(size), MinTileSize, Size);
            int level = Log2(Size / size);
            Point tile = origin;
            while (level > 0)
            {
                int parentSize = size * 2;
                Point parent = new Point(tile.X / parentSize * parentSize, tile.Y / parentSize * parentSize);
                Point a = parent, b = new Point(parent.X + size, parent.Y), c = new Point(parent.X, parent.Y + size),
                    d = new Point(parent.X + size, parent.Y + size);
                int freeSiblings = 0;
                foreach (Point sibling in new[] { a, b, c, d })
                    if (sibling == tile || _free[level].Contains(sibling)) freeSiblings++;
                if (freeSiblings < 4) break;
                _free[level].Remove(a); _free[level].Remove(b); _free[level].Remove(c); _free[level].Remove(d);
                tile = parent;
                size = parentSize;
                level--;
            }
            _free[level].Add(tile);
        }

        private static Point First(HashSet<Point> set)
        {
            //Lowest address first keeps allocations packed towards the top-left
            Point best = new Point(int.MaxValue, int.MaxValue);
            foreach (Point p in set)
                if (p.Y < best.Y || p.Y == best.Y && p.X < best.X) best = p;
            return best;
        }

        public static int NextPowerOfTwo(int value)
        {
            int p = 1;
            while (p < value && p < (1 << 30)) p <<= 1;
            return p;
        }

        private static int Log2(int value)
        {
            int l = 0;
            while ((1 << (l + 1)) <= value) l++;
            return l;
        }

        public void Dispose()
        {
            Target?.Dispose();
            Target = null;
        }
    }
}
