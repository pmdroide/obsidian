using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Physics
{
    /// <summary>
    /// Draws a <see cref="Cloth"/>. Every vertex buffer of the gameobject's model gets a same-layout dynamic copy
    /// whose positions, normals and tangent frames follow the particles; <see cref="Renderer.Helper.MeshMaterialLibrary"/>
    /// draws it in place of the shared buffer (via <c>TransformMatrix.Cloth</c>, like a skinned mesh), so the
    /// G-buffer, shadow, forward and ID/outline passes all see the cloth. Game thread only.
    /// </summary>
    public sealed class ClothMeshInstance : IDisposable
    {
        private sealed class Target
        {
            public DynamicVertexBuffer Buffer;
            public byte[] Output;
            public int First; // index of the buffer's first vertex in the model-wide vertex list
            public int Count, Stride;
            public int PositionOffset, NormalOffset = -1, TangentOffset = -1, BinormalOffset = -1;
            public Vector2[] TexCoords;
            // Buffer-local vertex indices of every triangle drawn from this buffer.
            public int[] Triangles;
            // Rest normals point along or against the triangles' winding; the stored tangent frame's handedness.
            public float NormalSign = 1, TangentSign = 1;
            public float[] BinormalSigns;
            public Vector3[] Normals, Tangents;
        }

        private readonly Dictionary<VertexBuffer, Target> _targets = new(ReferenceEqualityComparer.Instance);
        private Vector3[] _local = Array.Empty<Vector3>();

        /// <summary>The particles of the model's triangles (all buffers welded together).</summary>
        public ClothTopology Topology { get; }

        public ClothMeshInstance(GraphicsDevice graphicsDevice, Model model)
        {
            var vertices = new List<Vector3>();
            var indices = new List<int>();
            var partTriangles = new Dictionary<Target, List<int>>();
            foreach (ModelMesh mesh in model.Meshes)
                foreach (ModelMeshPart part in mesh.MeshParts)
                {
                    VertexBuffer shared = part.VertexBuffer;
                    if (shared == null || part.IndexBuffer == null || part.PrimitiveCount <= 0) continue;
                    if (!_targets.TryGetValue(shared, out Target target))
                    {
                        target = CreateTarget(graphicsDevice, shared, vertices.Count);
                        if (target == null) continue;
                        _targets.Add(shared, target);
                        partTriangles.Add(target, new List<int>());
                        for (int v = 0; v < target.Count; v++)
                            vertices.Add(Unsafe.ReadUnaligned<Vector3>(ref target.Output[v * target.Stride + target.PositionOffset]));
                    }
                    foreach (int index in ReadIndices(part))
                    {
                        int local = part.VertexOffset + index;
                        if (local < 0 || local >= target.Count) continue;
                        partTriangles[target].Add(local);
                        indices.Add(target.First + local);
                    }
                }

            // Drop a partial triangle left by an out-of-range index.
            indices.RemoveRange(indices.Count - indices.Count % 3, indices.Count % 3);
            Topology = ClothTopology.Build(vertices, indices);
            foreach (var (target, triangles) in partTriangles)
            {
                triangles.RemoveRange(triangles.Count - triangles.Count % 3, triangles.Count % 3);
                target.Triangles = triangles.ToArray();
                MeasureRestFrame(target);
            }
        }

        /// <summary>False when the model has no triangles to simulate.</summary>
        public bool IsValid => Topology.ParticleCount > 0 && Topology.Triangles.Length > 0;

        /// <summary>The cloth's replacement for a shared vertex buffer, or null to draw it unchanged.</summary>
        public VertexBuffer Resolve(VertexBuffer shared) =>
            shared != null && _targets.TryGetValue(shared, out var target) ? target.Buffer : null;

        /// <summary>CPU copy of the vertices uploaded for a shared buffer (checks read it back).</summary>
        internal byte[] VertexData(VertexBuffer shared) =>
            shared != null && _targets.TryGetValue(shared, out var target) ? target.Output : null;

        /// <summary>Moves the vertices to the cloth's world-space <paramref name="particles"/>, drawn with <paramref name="world"/>.</summary>
        public void Update(ReadOnlySpan<Vector3> particles, Matrix world)
        {
            Matrix inverse = Matrix.Invert(world);
            if (_local.Length != particles.Length) _local = new Vector3[particles.Length];
            for (int i = 0; i < particles.Length; i++) _local[i] = Vector3.Transform(particles[i], inverse);

            foreach (Target target in _targets.Values)
            {
                Write(target, Topology.VertexParticles);
                target.Buffer.SetData(0, target.Output, 0, target.Output.Length, 1, SetDataOptions.Discard);
            }
        }

        private void Write(Target target, int[] vertexParticles)
        {
            byte[] output = target.Output;
            for (int v = 0; v < target.Count; v++)
            {
                int particle = vertexParticles[target.First + v];
                if (particle < 0) continue;
                Unsafe.WriteUnaligned(ref output[v * target.Stride + target.PositionOffset], _local[particle]);
            }
            if (target.NormalOffset < 0) return;

            Accumulate(target, target.Normals, target.Tangents);
            for (int v = 0; v < target.Count; v++)
            {
                if (vertexParticles[target.First + v] < 0) continue;
                Vector3 normal = Normalized(target.Normals[v] * target.NormalSign, Vector3.UnitZ);
                int vertex = v * target.Stride;
                Unsafe.WriteUnaligned(ref output[vertex + target.NormalOffset], normal);
                if (target.TangentOffset < 0) continue;

                Vector3 tangent = target.Tangents[v] * target.TangentSign;
                tangent = Normalized(tangent - normal * Vector3.Dot(normal, tangent), Perpendicular(normal));
                Unsafe.WriteUnaligned(ref output[vertex + target.TangentOffset], tangent);
                if (target.BinormalOffset >= 0)
                    Unsafe.WriteUnaligned(ref output[vertex + target.BinormalOffset], Vector3.Cross(normal, tangent) * target.BinormalSigns[v]);
            }
        }

        /// <summary>Area-weighted face normals and UV tangents (dP/du) summed per vertex, from the current positions.</summary>
        private void Accumulate(Target target, Vector3[] normals, Vector3[] tangents)
        {
            Array.Clear(normals);
            Array.Clear(tangents);
            int[] vertexParticles = Topology.VertexParticles;
            int[] triangles = target.Triangles;
            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                int i0 = triangles[t], i1 = triangles[t + 1], i2 = triangles[t + 2];
                int p0 = vertexParticles[target.First + i0], p1 = vertexParticles[target.First + i1], p2 = vertexParticles[target.First + i2];
                if (p0 < 0 || p1 < 0 || p2 < 0) continue;
                Vector3 e1 = _local[p1] - _local[p0], e2 = _local[p2] - _local[p0];
                Vector3 face = Vector3.Cross(e1, e2);
                normals[i0] += face;
                normals[i1] += face;
                normals[i2] += face;

                if (target.TexCoords == null) continue;
                Vector2 uv1 = target.TexCoords[i1] - target.TexCoords[i0], uv2 = target.TexCoords[i2] - target.TexCoords[i0];
                float r = uv1.X * uv2.Y - uv2.X * uv1.Y;
                if (Math.Abs(r) < 1e-12f) continue;
                Vector3 tangent = (e1 * uv2.Y - e2 * uv1.Y) / r;
                tangents[i0] += tangent;
                tangents[i1] += tangent;
                tangents[i2] += tangent;
            }
        }

        /// <summary>
        /// Compares the frames this class computes for the rest pose with the ones the content pipeline stored,
        /// so the moving cloth keeps the model's normal direction and tangent handedness.
        /// </summary>
        private void MeasureRestFrame(Target target)
        {
            if (target.NormalOffset < 0) return;
            target.Normals = new Vector3[target.Count];
            target.Tangents = new Vector3[target.Count];
            target.BinormalSigns = new float[target.Count];
            if (_local.Length != Topology.ParticleCount) _local = new Vector3[Topology.ParticleCount];
            Array.Copy(Topology.RestPositions, _local, Topology.ParticleCount);
            Accumulate(target, target.Normals, target.Tangents);

            float normalAgreement = 0, tangentAgreement = 0;
            byte[] data = target.Output;
            for (int v = 0; v < target.Count; v++)
            {
                int vertex = v * target.Stride;
                Vector3 normal = Unsafe.ReadUnaligned<Vector3>(ref data[vertex + target.NormalOffset]);
                normalAgreement += Vector3.Dot(target.Normals[v], normal);
                target.BinormalSigns[v] = 1;
                if (target.TangentOffset < 0) continue;
                Vector3 tangent = Unsafe.ReadUnaligned<Vector3>(ref data[vertex + target.TangentOffset]);
                tangentAgreement += Vector3.Dot(target.Tangents[v], tangent);
                if (target.BinormalOffset < 0) continue;
                Vector3 binormal = Unsafe.ReadUnaligned<Vector3>(ref data[vertex + target.BinormalOffset]);
                target.BinormalSigns[v] = Vector3.Dot(Vector3.Cross(normal, tangent), binormal) < 0 ? -1 : 1;
            }
            target.NormalSign = normalAgreement < 0 ? -1 : 1;
            target.TangentSign = tangentAgreement < 0 ? -1 : 1;
        }

        public void Dispose()
        {
            foreach (Target target in _targets.Values) target.Buffer.Dispose();
            _targets.Clear();
        }

        private static Target CreateTarget(GraphicsDevice graphicsDevice, VertexBuffer shared, int first)
        {
            VertexDeclaration declaration = shared.VertexDeclaration;
            var target = new Target { First = first, Count = shared.VertexCount, Stride = declaration.VertexStride, PositionOffset = -1 };
            int texCoord = -1;
            foreach (VertexElement element in declaration.GetVertexElements())
            {
                if (element.UsageIndex != 0) continue;
                bool vector3 = element.VertexElementFormat == VertexElementFormat.Vector3;
                switch (element.VertexElementUsage)
                {
                    case VertexElementUsage.Position when vector3: target.PositionOffset = element.Offset; break;
                    case VertexElementUsage.Normal when vector3: target.NormalOffset = element.Offset; break;
                    case VertexElementUsage.Tangent when vector3: target.TangentOffset = element.Offset; break;
                    case VertexElementUsage.Binormal when vector3: target.BinormalOffset = element.Offset; break;
                    case VertexElementUsage.TextureCoordinate when element.VertexElementFormat == VertexElementFormat.Vector2:
                        texCoord = element.Offset;
                        break;
                }
            }
            if (target.PositionOffset < 0) return null;

            target.Output = new byte[target.Count * target.Stride];
            shared.GetData(0, target.Output, 0, target.Output.Length, 1);
            if (texCoord >= 0)
            {
                target.TexCoords = new Vector2[target.Count];
                for (int v = 0; v < target.Count; v++)
                    target.TexCoords[v] = Unsafe.ReadUnaligned<Vector2>(ref target.Output[v * target.Stride + texCoord]);
            }
            target.Buffer = new DynamicVertexBuffer(graphicsDevice, declaration, target.Count, BufferUsage.WriteOnly);
            target.Buffer.SetData(target.Output);
            return target;
        }

        private static int[] ReadIndices(ModelMeshPart part)
        {
            int count = part.PrimitiveCount * 3;
            var indices = new int[count];
            if (part.IndexBuffer.IndexElementSize == IndexElementSize.ThirtyTwoBits)
            {
                part.IndexBuffer.GetData(part.StartIndex * 4, indices, 0, count);
                return indices;
            }
            var shorts = new ushort[count];
            part.IndexBuffer.GetData(part.StartIndex * 2, shorts, 0, count);
            for (int i = 0; i < count; i++) indices[i] = shorts[i];
            return indices;
        }

        private static Vector3 Normalized(Vector3 v, Vector3 fallback)
        {
            float lengthSquared = v.LengthSquared();
            return lengthSquared > 1e-20f ? v / MathF.Sqrt(lengthSquared) : fallback;
        }

        private static Vector3 Perpendicular(Vector3 normal) =>
            Vector3.Normalize(Vector3.Cross(normal, Math.Abs(normal.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitX));
    }
}
