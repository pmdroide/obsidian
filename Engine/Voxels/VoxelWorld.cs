using System;
using System.Collections.Generic;
using Engine.Logic;
using Microsoft.Xna.Framework;

namespace Engine.Voxels
{
    /// <summary>A block position in the voxel grid: X/Y across, Z up, (0,0,0) at the world's lowest corner.</summary>
    public readonly record struct VoxelCoord(int X, int Y, int Z)
    {
        public static VoxelCoord operator +(VoxelCoord a, VoxelCoord b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public override string ToString() => $"({X}, {Y}, {Z})";
    }

    /// <summary>What <see cref="VoxelWorld.Raycast(Vector3, Vector3, float, out VoxelHit)"/> found.</summary>
    public struct VoxelHit
    {
        /// <summary>The solid block the ray hit.</summary>
        public VoxelCoord Block;
        /// <summary>The empty cell in front of the face that was hit: where a placed block goes.</summary>
        public VoxelCoord Adjacent;
        public VoxelBlock Type;
        /// <summary>World-space normal of the face that was hit (one of the six axis directions).</summary>
        public Vector3 Normal;
        /// <summary>World-space point where the ray entered the block.</summary>
        public Vector3 Point;
        /// <summary>Distance from the ray origin, in metres.</summary>
        public float Distance;
    }

    /// <summary>
    /// The voxel-world part of a scene's <see cref="EnvironmentSettings"/>, clamped. Two equal values generate
    /// the same world, so <see cref="VoxelSystem"/> regenerates only when this changes.
    /// </summary>
    public readonly record struct VoxelWorldSettings(
        VoxelTerrain Terrain, int Seed, int Size, int Height, float BlockSize, float BaseHeight,
        bool Caves, float Trees, float SeaLevel, bool Collision)
    {
        public const int MinSize = 32, MaxSize = 256, SizeStep = 32;
        public const int MinHeight = 16, MaxHeight = 128, HeightStep = 16;
        public const float MinBlockSize = 0.25f, MaxBlockSize = 4f;

        /// <summary>The settings of an enabled voxel world, or null when <see cref="EnvironmentSettings.Voxels"/> is off.</summary>
        public static VoxelWorldSettings? From(EnvironmentSettings environment)
        {
            if (environment == null || !environment.Voxels) return null;
            return new VoxelWorldSettings(
                Enum.IsDefined(environment.VoxelTerrain) ? environment.VoxelTerrain : VoxelTerrain.Hills,
                environment.VoxelSeed,
                ClampSize(environment.VoxelWorldSize),
                ClampHeight(environment.VoxelWorldHeight),
                ClampBlockSize(environment.VoxelBlockSize),
                float.IsFinite(environment.VoxelBaseHeight) ? Math.Clamp(environment.VoxelBaseHeight, -1000, 1000) : 0,
                environment.VoxelCaves,
                float.IsFinite(environment.VoxelTrees) ? Math.Clamp(environment.VoxelTrees, 0, 1) : 0,
                float.IsFinite(environment.VoxelSeaLevel) ? Math.Clamp(environment.VoxelSeaLevel, 0, 0.9f) : 0,
                environment.VoxelCollision);
        }

        public static int ClampSize(int size) =>
            Math.Clamp((int)Math.Round(size / (double)SizeStep) * SizeStep, MinSize, MaxSize);

        public static int ClampHeight(int height) =>
            Math.Clamp((int)Math.Round(height / (double)HeightStep) * HeightStep, MinHeight, MaxHeight);

        public static float ClampBlockSize(float size) =>
            float.IsFinite(size) ? Math.Clamp(size, MinBlockSize, MaxBlockSize) : 1;

        /// <summary>Height in blocks of the water surface; 0 = no sea.</summary>
        public int SeaBlocks => SeaLevel > 0 ? Math.Clamp((int)MathF.Round(SeaLevel * Height), 1, Height - 1) : 0;
    }

    /// <summary>
    /// A block grid of one byte per voxel, split into <see cref="ChunkSize"/>³ chunks for meshing. Centred on the
    /// world origin in X/Y; its floor is at <see cref="VoxelWorldSettings.BaseHeight"/>. Edits mark the chunks
    /// they touch dirty, and <see cref="VoxelSystem"/> remeshes those (and rebuilds their colliders) before the
    /// next physics step. Game thread only once generated.
    /// </summary>
    public sealed class VoxelWorld
    {
        public const int ChunkSize = 32;

        private readonly byte[] _blocks;
        private readonly bool[] _dirty;
        private readonly List<int> _dirtyList = new List<int>();

        public VoxelWorld(VoxelWorldSettings settings)
        {
            Settings = settings;
            SizeX = SizeY = settings.Size;
            SizeZ = settings.Height;
            BlockSize = settings.BlockSize;
            Origin = new Vector3(-SizeX * BlockSize / 2, -SizeY * BlockSize / 2, settings.BaseHeight);
            SeaLevel = settings.SeaBlocks;
            ChunksX = (SizeX + ChunkSize - 1) / ChunkSize;
            ChunksY = (SizeY + ChunkSize - 1) / ChunkSize;
            ChunksZ = (SizeZ + ChunkSize - 1) / ChunkSize;
            _blocks = new byte[SizeX * SizeY * SizeZ];
            _dirty = new bool[ChunkCount];
        }

        public VoxelWorldSettings Settings { get; }
        public int SizeX { get; }
        public int SizeY { get; }
        public int SizeZ { get; }
        /// <summary>Edge length of one block in metres.</summary>
        public float BlockSize { get; }
        /// <summary>World-space position of block (0,0,0)'s lowest corner.</summary>
        public Vector3 Origin { get; }
        /// <summary>Blocks below this height are under water; 0 = no sea.</summary>
        public int SeaLevel { get; }
        public bool HasSea => SeaLevel > 0;
        /// <summary>
        /// World Z of the sea surface (meaningful while <see cref="HasSea"/>): a little above the tops of the
        /// highest underwater blocks (Z = <see cref="SeaLevel"/> - 1), so they are covered without z-fighting.
        /// </summary>
        public float SeaSurfaceZ => Origin.Z + (SeaLevel + 0.15f) * BlockSize;

        public int ChunksX { get; }
        public int ChunksY { get; }
        public int ChunksZ { get; }
        public int ChunkCount => ChunksX * ChunksY * ChunksZ;

        /// <summary>Changes on every block edit, so scripts and caches can tell the world changed.</summary>
        public int Version { get; private set; }
        /// <summary>Blocks changed since the world was generated.</summary>
        public int EditCount { get; private set; }

        public BoundingBox Bounds => new BoundingBox(Origin, Origin + new Vector3(SizeX, SizeY, SizeZ) * BlockSize);

        public bool InBounds(int x, int y, int z) =>
            (uint)x < (uint)SizeX && (uint)y < (uint)SizeY && (uint)z < (uint)SizeZ;

        public bool InBounds(VoxelCoord c) => InBounds(c.X, c.Y, c.Z);

        private int Index(int x, int y, int z) => x + SizeX * (y + SizeY * z);

        /// <summary>The block at a cell; Air outside the world.</summary>
        public VoxelBlock GetBlock(int x, int y, int z) =>
            InBounds(x, y, z) ? (VoxelBlock)_blocks[Index(x, y, z)] : VoxelBlock.Air;

        public VoxelBlock GetBlock(VoxelCoord c) => GetBlock(c.X, c.Y, c.Z);

        /// <summary>The block containing a world-space point; Air outside the world.</summary>
        public VoxelBlock GetBlock(Vector3 world) => GetBlock(WorldToBlock(world));

        public bool IsSolid(int x, int y, int z) => VoxelBlocks.IsSolid(GetBlock(x, y, z));

        /// <summary>The bottom layer (Z = 0) can't be changed, so nothing falls out of the world.</summary>
        public bool IsEditable(VoxelCoord c) => InBounds(c) && c.Z > 0;

        /// <summary>
        /// Changes one block. False when the cell is outside the world, in the bottom layer, or already holds
        /// <paramref name="block"/>. The chunk is remeshed before the next physics step.
        /// </summary>
        public bool SetBlock(int x, int y, int z, VoxelBlock block)
        {
            if (!IsEditable(new VoxelCoord(x, y, z)) || (byte)block >= VoxelBlocks.Count) return false;
            int i = Index(x, y, z);
            if (_blocks[i] == (byte)block) return false;
            _blocks[i] = (byte)block;
            Version++;
            EditCount++;
            MarkDirty(x, y, z);
            return true;
        }

        public bool SetBlock(VoxelCoord c, VoxelBlock block) => SetBlock(c.X, c.Y, c.Z, block);

        public bool SetBlock(Vector3 world, VoxelBlock block) => SetBlock(WorldToBlock(world), block);

        /// <summary>Generator access: no dirty marking, no bottom-layer guard.</summary>
        internal void SetGenerated(int x, int y, int z, VoxelBlock block) => _blocks[Index(x, y, z)] = (byte)block;

        /// <summary>
        /// Sets every editable cell in the box between two corners (inclusive) to <paramref name="block"/>.
        /// Returns how many blocks changed.
        /// </summary>
        public int FillBox(VoxelCoord a, VoxelCoord b, VoxelBlock block)
        {
            int changed = 0;
            for (int z = Math.Max(Math.Min(a.Z, b.Z), 1); z <= Math.Min(Math.Max(a.Z, b.Z), SizeZ - 1); z++)
            for (int y = Math.Max(Math.Min(a.Y, b.Y), 0); y <= Math.Min(Math.Max(a.Y, b.Y), SizeY - 1); y++)
            for (int x = Math.Max(Math.Min(a.X, b.X), 0); x <= Math.Min(Math.Max(a.X, b.X), SizeX - 1); x++)
                if (SetBlock(x, y, z, block)) changed++;
            return changed;
        }

        /// <summary>
        /// Sets every editable block whose centre is within <paramref name="radius"/> metres of a world-space
        /// point (Air digs a crater). Returns how many blocks changed.
        /// </summary>
        public int FillSphere(Vector3 center, float radius, VoxelBlock block)
        {
            if (!(radius > 0)) return 0;
            VoxelCoord min = WorldToBlock(center - new Vector3(radius)), max = WorldToBlock(center + new Vector3(radius));
            int changed = 0;
            float r2 = radius * radius;
            for (int z = Math.Max(min.Z, 1); z <= Math.Min(max.Z, SizeZ - 1); z++)
            for (int y = Math.Max(min.Y, 0); y <= Math.Min(max.Y, SizeY - 1); y++)
            for (int x = Math.Max(min.X, 0); x <= Math.Min(max.X, SizeX - 1); x++)
                if (Vector3.DistanceSquared(BlockCenter(new VoxelCoord(x, y, z)), center) <= r2 && SetBlock(x, y, z, block))
                    changed++;
            return changed;
        }

        public VoxelCoord WorldToBlock(Vector3 world)
        {
            Vector3 p = (world - Origin) / BlockSize;
            return new VoxelCoord((int)MathF.Floor(p.X), (int)MathF.Floor(p.Y), (int)MathF.Floor(p.Z));
        }

        /// <summary>World-space lowest corner of a block.</summary>
        public Vector3 BlockToWorld(VoxelCoord c) => Origin + new Vector3(c.X, c.Y, c.Z) * BlockSize;

        public Vector3 BlockCenter(VoxelCoord c) => Origin + (new Vector3(c.X, c.Y, c.Z) + new Vector3(0.5f)) * BlockSize;

        public BoundingBox BlockBounds(VoxelCoord c) => new BoundingBox(BlockToWorld(c), BlockToWorld(c) + new Vector3(BlockSize));

        /// <summary>Z (in blocks) of the highest solid block in a column, or -1 when the column is empty.</summary>
        public int SurfaceHeight(int x, int y)
        {
            if ((uint)x >= (uint)SizeX || (uint)y >= (uint)SizeY) return -1;
            for (int z = SizeZ - 1; z >= 0; z--)
                if (_blocks[Index(x, y, z)] != 0) return z;
            return -1;
        }

        /// <summary>
        /// The top of the highest solid block under (or over) a world-space X/Y: the point a character would
        /// stand on. False outside the world or over an empty column.
        /// </summary>
        public bool TryGetSurface(Vector3 world, out Vector3 surface)
        {
            VoxelCoord c = WorldToBlock(world);
            int z = SurfaceHeight(c.X, c.Y);
            surface = new Vector3(world.X, world.Y, Origin.Z + (z + 1) * BlockSize);
            return z >= 0;
        }

        /// <summary>How many cells hold <paramref name="block"/>.</summary>
        public int Count(VoxelBlock block)
        {
            int count = 0;
            byte b = (byte)block;
            for (int i = 0; i < _blocks.Length; i++)
                if (_blocks[i] == b) count++;
            return count;
        }

        /// <summary>
        /// The first solid block along a ray (a grid walk, no physics involved), within <paramref name="maxDistance"/>
        /// metres. A ray starting inside a solid block hits that block.
        /// </summary>
        public bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, out VoxelHit hit)
        {
            hit = default;
            if (!(maxDistance > 0) || !(direction.LengthSquared() > 1e-12f)) return false;
            Vector3 dir = Vector3.Normalize(direction);
            Vector3 o = (origin - Origin) / BlockSize;
            float tEnter = 0, tExit = maxDistance / BlockSize;
            int enterAxis = -1;
            if (!Slab(o.X, dir.X, SizeX, 0, ref tEnter, ref tExit, ref enterAxis) ||
                !Slab(o.Y, dir.Y, SizeY, 1, ref tEnter, ref tExit, ref enterAxis) ||
                !Slab(o.Z, dir.Z, SizeZ, 2, ref tEnter, ref tExit, ref enterAxis))
                return false;

            Vector3 start = o + dir * tEnter;
            int x = Math.Clamp((int)MathF.Floor(start.X), 0, SizeX - 1);
            int y = Math.Clamp((int)MathF.Floor(start.Y), 0, SizeY - 1);
            int z = Math.Clamp((int)MathF.Floor(start.Z), 0, SizeZ - 1);
            int stepX = Math.Sign(dir.X), stepY = Math.Sign(dir.Y), stepZ = Math.Sign(dir.Z);
            float tDeltaX = stepX != 0 ? MathF.Abs(1 / dir.X) : float.PositiveInfinity;
            float tDeltaY = stepY != 0 ? MathF.Abs(1 / dir.Y) : float.PositiveInfinity;
            float tDeltaZ = stepZ != 0 ? MathF.Abs(1 / dir.Z) : float.PositiveInfinity;
            // Absolute ray parameter (block units from the origin) at the next X/Y/Z cell boundary.
            float tMaxX = stepX > 0 ? (x + 1 - o.X) / dir.X : stepX < 0 ? (x - o.X) / dir.X : float.PositiveInfinity;
            float tMaxY = stepY > 0 ? (y + 1 - o.Y) / dir.Y : stepY < 0 ? (y - o.Y) / dir.Y : float.PositiveInfinity;
            float tMaxZ = stepZ > 0 ? (z + 1 - o.Z) / dir.Z : stepZ < 0 ? (z - o.Z) / dir.Z : float.PositiveInfinity;
            float t = tEnter;
            int axis = enterAxis;

            while (true)
            {
                byte block = _blocks[Index(x, y, z)];
                if (block != 0)
                {
                    Vector3 normal = axis switch
                    {
                        0 => new Vector3(-stepX, 0, 0),
                        1 => new Vector3(0, -stepY, 0),
                        2 => new Vector3(0, 0, -stepZ),
                        _ => -DominantAxis(dir),
                    };
                    hit.Block = new VoxelCoord(x, y, z);
                    hit.Adjacent = new VoxelCoord(x + (int)normal.X, y + (int)normal.Y, z + (int)normal.Z);
                    hit.Type = (VoxelBlock)block;
                    hit.Normal = normal;
                    hit.Distance = t * BlockSize;
                    hit.Point = origin + dir * hit.Distance;
                    return true;
                }

                if (tMaxX < tMaxY && tMaxX < tMaxZ)
                {
                    if (tMaxX > tExit) return false;
                    t = tMaxX;
                    x += stepX;
                    tMaxX += tDeltaX;
                    axis = 0;
                }
                else if (tMaxY < tMaxZ)
                {
                    if (tMaxY > tExit) return false;
                    t = tMaxY;
                    y += stepY;
                    tMaxY += tDeltaY;
                    axis = 1;
                }
                else
                {
                    if (tMaxZ > tExit) return false;
                    t = tMaxZ;
                    z += stepZ;
                    tMaxZ += tDeltaZ;
                    axis = 2;
                }
                if (!InBounds(x, y, z)) return false;
            }
        }

        public bool Raycast(Ray ray, float maxDistance, out VoxelHit hit) =>
            Raycast(ray.Position, ray.Direction, maxDistance, out hit);

        private static Vector3 DominantAxis(Vector3 d)
        {
            Vector3 a = new Vector3(MathF.Abs(d.X), MathF.Abs(d.Y), MathF.Abs(d.Z));
            if (a.X >= a.Y && a.X >= a.Z) return new Vector3(MathF.Sign(d.X), 0, 0);
            if (a.Y >= a.Z) return new Vector3(0, MathF.Sign(d.Y), 0);
            return new Vector3(0, 0, MathF.Sign(d.Z) == 0 ? 1 : MathF.Sign(d.Z));
        }

        // Clips the ray to the slab [0, size] on one axis.
        private static bool Slab(float o, float d, float size, int axis, ref float tMin, ref float tMax, ref int enterAxis)
        {
            if (MathF.Abs(d) < 1e-9f) return o >= 0 && o <= size;
            float t0 = -o / d, t1 = (size - o) / d;
            if (t0 > t1) (t0, t1) = (t1, t0);
            if (t0 > tMin)
            {
                tMin = t0;
                enterAxis = axis;
            }
            if (t1 < tMax) tMax = t1;
            return tMin <= tMax;
        }

        ////////////////////////////////////////////////////////////////////////////////
        //  CHUNKS
        ////////////////////////////////////////////////////////////////////////////////

        public int ChunkIndex(int cx, int cy, int cz) => cx + ChunksX * (cy + ChunksY * cz);

        public void ChunkCoord(int index, out int cx, out int cy, out int cz)
        {
            cx = index % ChunksX;
            cy = index / ChunksX % ChunksY;
            cz = index / (ChunksX * ChunksY);
        }

        /// <summary>Chunks edited since the last <see cref="TakeDirtyChunks"/>.</summary>
        public int DirtyChunkCount => _dirtyList.Count;

        // A face on a chunk border belongs to the neighbouring chunk's mesh too.
        private void MarkDirty(int x, int y, int z)
        {
            int cx = x / ChunkSize, cy = y / ChunkSize, cz = z / ChunkSize;
            int lx = x % ChunkSize, ly = y % ChunkSize, lz = z % ChunkSize;
            Mark(cx, cy, cz);
            if (lx == 0) Mark(cx - 1, cy, cz);
            if (lx == ChunkSize - 1) Mark(cx + 1, cy, cz);
            if (ly == 0) Mark(cx, cy - 1, cz);
            if (ly == ChunkSize - 1) Mark(cx, cy + 1, cz);
            if (lz == 0) Mark(cx, cy, cz - 1);
            if (lz == ChunkSize - 1) Mark(cx, cy, cz + 1);
        }

        private void Mark(int cx, int cy, int cz)
        {
            if ((uint)cx >= (uint)ChunksX || (uint)cy >= (uint)ChunksY || (uint)cz >= (uint)ChunksZ) return;
            int index = ChunkIndex(cx, cy, cz);
            if (_dirty[index]) return;
            _dirty[index] = true;
            _dirtyList.Add(index);
        }

        /// <summary>Hands out the edited chunks' indices and clears the list.</summary>
        public void TakeDirtyChunks(List<int> into)
        {
            into.Clear();
            into.AddRange(_dirtyList);
            foreach (int index in _dirtyList) _dirty[index] = false;
            _dirtyList.Clear();
        }
    }
}
