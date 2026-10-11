using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Voxels
{
    /// <summary>
    /// Vertex of a voxel chunk: what the G-buffer's normal-mapped techniques read (position, normal, UV, tangent
    /// frame). UVs are in blocks, so every block shows its texture once (the samplers wrap).
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct VoxelVertex : IVertexType
    {
        public Vector3 Position;
        public Vector3 Normal;
        public Vector2 TextureCoordinate;
        public Vector3 Tangent;
        public Vector3 Binormal;

        public static readonly VertexDeclaration Declaration = new VertexDeclaration(
            new VertexElement(0, VertexElementFormat.Vector3, VertexElementUsage.Position, 0),
            new VertexElement(12, VertexElementFormat.Vector3, VertexElementUsage.Normal, 0),
            new VertexElement(24, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0),
            new VertexElement(32, VertexElementFormat.Vector3, VertexElementUsage.Tangent, 0),
            new VertexElement(44, VertexElementFormat.Vector3, VertexElementUsage.Binormal, 0));

        VertexDeclaration IVertexType.VertexDeclaration => Declaration;
    }

    /// <summary>The triangles of one surface inside a chunk's index buffer.</summary>
    public readonly record struct VoxelMeshPart(VoxelSurface Surface, int StartIndex, int PrimitiveCount);

    /// <summary>CPU mesh of one chunk: render triangles grouped by surface, plus a coarser collision mesh.</summary>
    public sealed class VoxelChunkMesh
    {
        public int Chunk;
        public VoxelVertex[] Vertices;
        public int[] Indices;
        public VoxelMeshPart[] Parts;
        /// <summary>World-space collision triangles: every exposed face merged regardless of its look.</summary>
        public Vector3[] ColliderVertices;
        public int[] ColliderIndices;
        /// <summary>World-space bounds of the chunk's cells.</summary>
        public BoundingSphere Bounds;

        public bool IsEmpty => Indices.Length == 0;
        public int TriangleCount => Indices.Length / 3;
    }

    /// <summary>
    /// Greedy mesher: per chunk, per axis and side, finds the block faces that border air and merges equal
    /// neighbours into rectangles. Render faces merge per <see cref="VoxelSurface"/>; collision faces merge
    /// across surfaces. Triangles wind clockwise seen from outside, the convention of the engine's models,
    /// which is what both the rasterizer (culling counter-clockwise) and BEPU (one-sided triangles) expect.
    /// Reads the world only, so worlds being generated can be meshed on a worker thread.
    /// </summary>
    public static class VoxelMesher
    {
        public static VoxelChunkMesh Build(VoxelWorld world, int chunk)
        {
            world.ChunkCoord(chunk, out int cx, out int cy, out int cz);
            int s = VoxelWorld.ChunkSize;
            int[] start = { cx * s, cy * s, cz * s };
            int[] size =
            {
                Math.Min(s, world.SizeX - start[0]), Math.Min(s, world.SizeY - start[1]), Math.Min(s, world.SizeZ - start[2]),
            };

            var surfaces = new SurfaceBuilder[VoxelBlocks.SurfaceCount];
            var colliderVertices = new List<Vector3>();
            var colliderIndices = new List<int>();
            var renderMask = new int[s * s];
            var colliderMask = new int[s * s];
            Span<int> p = stackalloc int[3];

            for (int d = 0; d < 3; d++)
            {
                int u = (d + 1) % 3, v = (d + 2) % 3;
                int width = size[u], height = size[v];
                for (int side = -1; side <= 1; side += 2)
                {
                    VoxelFace face = d == 2 ? (side > 0 ? VoxelFace.Top : VoxelFace.Bottom) : VoxelFace.Side;
                    for (int layer = 0; layer < size[d]; layer++)
                    {
                        bool any = false;
                        for (int b = 0; b < height; b++)
                        for (int a = 0; a < width; a++)
                        {
                            p[d] = start[d] + layer;
                            p[u] = start[u] + a;
                            p[v] = start[v] + b;
                            VoxelBlock block = world.GetBlock(p[0], p[1], p[2]);
                            int key = 0;
                            if (VoxelBlocks.IsSolid(block))
                            {
                                p[d] += side;
                                if (!SolidForMeshing(world, p[0], p[1], p[2]))
                                    key = (int)VoxelBlocks.Surface(block, face) + 1;
                            }
                            renderMask[a + b * width] = key;
                            colliderMask[a + b * width] = key == 0 ? 0 : 1;
                            any |= key != 0;
                        }
                        if (!any) continue;

                        // The face plane: the block's far side when looking along +axis.
                        int plane = start[d] + layer + (side > 0 ? 1 : 0);
                        Greedy(renderMask, width, height, (key, a, b, w, h) =>
                        {
                            ref SurfaceBuilder builder = ref surfaces[key - 1];
                            builder ??= new SurfaceBuilder();
                            EmitRenderQuad(world, builder, d, u, v, side, plane, start[u] + a, start[v] + b, w, h);
                        });
                        Greedy(colliderMask, width, height, (_, a, b, w, h) =>
                            EmitColliderQuad(world, colliderVertices, colliderIndices, d, u, v, side, plane,
                                start[u] + a, start[v] + b, w, h));
                    }
                }
            }

            // One vertex/index buffer per chunk; each surface is a range of it.
            int vertexCount = 0, indexCount = 0;
            foreach (SurfaceBuilder builder in surfaces)
            {
                if (builder == null) continue;
                vertexCount += builder.Vertices.Count;
                indexCount += builder.Indices.Count;
            }
            var vertices = new VoxelVertex[vertexCount];
            var indices = new int[indexCount];
            var parts = new List<VoxelMeshPart>();
            int vertexBase = 0, indexBase = 0;
            for (int i = 0; i < surfaces.Length; i++)
            {
                SurfaceBuilder builder = surfaces[i];
                if (builder == null || builder.Indices.Count == 0) continue;
                builder.Vertices.CopyTo(vertices, vertexBase);
                for (int k = 0; k < builder.Indices.Count; k++) indices[indexBase + k] = builder.Indices[k] + vertexBase;
                parts.Add(new VoxelMeshPart((VoxelSurface)i, indexBase, builder.Indices.Count / 3));
                vertexBase += builder.Vertices.Count;
                indexBase += builder.Indices.Count;
            }

            Vector3 min = world.Origin + new Vector3(start[0], start[1], start[2]) * world.BlockSize;
            Vector3 max = min + new Vector3(size[0], size[1], size[2]) * world.BlockSize;
            return new VoxelChunkMesh
            {
                Chunk = chunk,
                Vertices = vertices,
                Indices = indices,
                Parts = parts.ToArray(),
                ColliderVertices = colliderVertices.ToArray(),
                ColliderIndices = colliderIndices.ToArray(),
                Bounds = new BoundingSphere((min + max) / 2, Vector3.Distance(min, max) / 2),
            };
        }

        // Below the world counts as solid (nobody sees the underside); beside and above it is open air.
        private static bool SolidForMeshing(VoxelWorld world, int x, int y, int z) =>
            z < 0 || world.IsSolid(x, y, z);

        private sealed class SurfaceBuilder
        {
            public readonly List<VoxelVertex> Vertices = new List<VoxelVertex>();
            public readonly List<int> Indices = new List<int>();
        }

        /// <summary>Merges equal non-zero mask cells into rectangles, row by row, and clears what it emits.</summary>
        private static void Greedy(int[] mask, int width, int height, Action<int, int, int, int, int> emit)
        {
            for (int b = 0; b < height; b++)
            for (int a = 0; a < width;)
            {
                int key = mask[a + b * width];
                if (key == 0)
                {
                    a++;
                    continue;
                }
                int w = 1;
                while (a + w < width && mask[a + w + b * width] == key) w++;
                int h = 1;
                for (; b + h < height; h++)
                {
                    bool rowMatches = true;
                    for (int k = 0; k < w; k++)
                        if (mask[a + k + (b + h) * width] != key)
                        {
                            rowMatches = false;
                            break;
                        }
                    if (!rowMatches) break;
                }
                emit(key, a, b, w, h);
                for (int hh = 0; hh < h; hh++)
                for (int k = 0; k < w; k++)
                    mask[a + k + (b + hh) * width] = 0;
                a += w;
            }
        }

        // Corner (in block units) of a face rectangle: d = plane axis, u/v = the rectangle's axes.
        private static Vector3 Corner(int d, int u, int v, int plane, int a, int b)
        {
            Span<float> c = stackalloc float[3];
            c[d] = plane;
            c[u] = a;
            c[v] = b;
            return new Vector3(c[0], c[1], c[2]);
        }

        private static void EmitRenderQuad(VoxelWorld world, SurfaceBuilder builder, int d, int u, int v, int side,
            int plane, int a, int b, int w, int h)
        {
            Vector3 normal = Axis(d) * side;
            // Texture axes: tops/bottoms use X/Y; sides run along the ground with the texture's top facing up.
            Vector3 tangent, binormal;
            if (d == 2)
            {
                tangent = Vector3.UnitX;
                binormal = Vector3.UnitY;
            }
            else
            {
                tangent = d == 0 ? Vector3.UnitY : Vector3.UnitX;
                binormal = -Vector3.UnitZ;
            }

            Span<Vector3> corners = stackalloc Vector3[]
            {
                Corner(d, u, v, plane, a, b), Corner(d, u, v, plane, a + w, b),
                Corner(d, u, v, plane, a + w, b + h), Corner(d, u, v, plane, a, b + h),
            };
            int first = builder.Vertices.Count;
            foreach (Vector3 c in corners)
            {
                builder.Vertices.Add(new VoxelVertex
                {
                    Position = world.Origin + c * world.BlockSize,
                    Normal = normal,
                    TextureCoordinate = d == 2 ? new Vector2(c.X, c.Y) : new Vector2(d == 0 ? c.Y : c.X, -c.Z),
                    Tangent = tangent,
                    Binormal = binormal,
                });
            }
            AddQuadIndices(builder.Indices, first, corners, normal);
        }

        private static void EmitColliderQuad(VoxelWorld world, List<Vector3> vertices, List<int> indices,
            int d, int u, int v, int side, int plane, int a, int b, int w, int h)
        {
            Span<Vector3> corners = stackalloc Vector3[]
            {
                Corner(d, u, v, plane, a, b), Corner(d, u, v, plane, a + w, b),
                Corner(d, u, v, plane, a + w, b + h), Corner(d, u, v, plane, a, b + h),
            };
            int first = vertices.Count;
            foreach (Vector3 c in corners) vertices.Add(world.Origin + c * world.BlockSize);
            AddQuadIndices(indices, first, corners, Axis(d) * side);
        }

        /// <summary>Two triangles for corners 0-1-2-3 around the rectangle, wound clockwise seen from the normal side.</summary>
        private static void AddQuadIndices(List<int> indices, int first, ReadOnlySpan<Vector3> corners, Vector3 normal)
        {
            bool counterClockwise = Vector3.Dot(Vector3.Cross(corners[1] - corners[0], corners[2] - corners[0]), normal) > 0;
            if (counterClockwise)
            {
                indices.Add(first); indices.Add(first + 2); indices.Add(first + 1);
                indices.Add(first); indices.Add(first + 3); indices.Add(first + 2);
            }
            else
            {
                indices.Add(first); indices.Add(first + 1); indices.Add(first + 2);
                indices.Add(first); indices.Add(first + 2); indices.Add(first + 3);
            }
        }

        private static Vector3 Axis(int d) => d == 0 ? Vector3.UnitX : d == 1 ? Vector3.UnitY : Vector3.UnitZ;
    }
}
