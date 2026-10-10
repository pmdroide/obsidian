using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Animation
{
    /// <summary>
    /// CPU skinning for one entity. Every vertex buffer of the model gets a same-layout dynamic
    /// copy holding the posed vertices; <see cref="Renderer.Helper.MeshMaterialLibrary"/> draws
    /// with it instead of the shared buffer (via <c>TransformMatrix.Skin</c>), so the G-buffer,
    /// shadow, forward and ID/outline passes all see the pose without skinned shader variants.
    /// Game thread only.
    /// </summary>
    public sealed class SkinnedMeshInstance : IDisposable
    {
        // Vertices per parallel work item.
        private const int ChunkSize = 2048;

        private sealed class Target
        {
            public SkinSource Source;
            public DynamicVertexBuffer Buffer;
            public byte[] Output;
        }

        private readonly Dictionary<VertexBuffer, Target> _targets = new(ReferenceEqualityComparer.Instance);
        // Work items: a target and the first vertex of a ChunkSize run.
        private readonly List<(Target Target, int First)> _chunks = new();

        public SkinnedMeshInstance(GraphicsDevice graphicsDevice, Model model)
        {
            foreach (ModelMesh mesh in model.Meshes)
                foreach (ModelMeshPart part in mesh.MeshParts)
                {
                    VertexBuffer shared = part.VertexBuffer;
                    if (shared == null || _targets.ContainsKey(shared)) continue;
                    SkinSource source = SkinSource.For(shared);
                    if (source == null) continue;
                    var target = new Target
                    {
                        Source = source,
                        Buffer = new DynamicVertexBuffer(graphicsDevice, shared.VertexDeclaration, shared.VertexCount, BufferUsage.WriteOnly),
                        Output = (byte[])source.Template.Clone(),
                    };
                    _targets.Add(shared, target);
                    for (int first = 0; first < source.VertexCount; first += ChunkSize) _chunks.Add((target, first));
                }
        }

        /// <summary>False when the model has no skinnable vertex buffer (no blend channels).</summary>
        public bool HasSkinnedBuffers => _targets.Count > 0;

        /// <summary>The posed replacement for a shared vertex buffer, or null to draw it unchanged.</summary>
        public VertexBuffer Resolve(VertexBuffer shared) =>
            shared != null && _targets.TryGetValue(shared, out var target) ? target.Buffer : null;

        /// <summary>CPU copy of the posed vertices uploaded for a shared buffer (checks read it back).</summary>
        internal byte[] PosedVertexData(VertexBuffer shared) =>
            shared != null && _targets.TryGetValue(shared, out var target) ? target.Output : null;

        /// <summary>Poses every buffer with model-space skin matrices (inverse bind * bone).</summary>
        public void Update(Matrix[] skinTransforms)
        {
            // One parallel pass over the chunks of every buffer, then the uploads.
            Parallel.For(0, _chunks.Count, c =>
            {
                (Target target, int first) = _chunks[c];
                int end = Math.Min(target.Source.VertexCount, first + ChunkSize);
                for (int v = first; v < end; v++)
                    target.Source.Skin(v, skinTransforms, target.Output);
            });
            foreach (Target target in _targets.Values)
                target.Buffer.SetData(0, target.Output, 0, target.Output.Length, 1, SetDataOptions.Discard);
        }

        public void Dispose()
        {
            foreach (Target target in _targets.Values) target.Buffer.Dispose();
            _targets.Clear();
            _chunks.Clear();
        }

        /// <summary>
        /// Bind-pose position and most heavily weighted bone of every skinned vertex of
        /// <paramref name="model"/> (each shared vertex buffer once). Unweighted vertices are skipped.
        /// </summary>
        public static void CollectDominantBones(Model model, List<Vector3> positions, List<int> bones)
        {
            var seen = new HashSet<VertexBuffer>(ReferenceEqualityComparer.Instance);
            foreach (ModelMesh mesh in model.Meshes)
                foreach (ModelMeshPart part in mesh.MeshParts)
                {
                    VertexBuffer shared = part.VertexBuffer;
                    if (shared == null || !seen.Add(shared)) continue;
                    SkinSource.For(shared)?.AppendDominant(positions, bones);
                }
        }

        /// <summary>Bind-pose vertices of one shared buffer, decoded once and shared by all instances.</summary>
        private sealed class SkinSource
        {
            private static readonly ConditionalWeakTable<VertexBuffer, SkinSource> Cache = new();

            public byte[] Template;
            public int VertexCount;
            private int _stride;
            private int _positionOffset, _normalOffset = -1, _tangentOffset = -1, _binormalOffset = -1;
            private Vector3[] _positions, _normals, _tangents, _binormals;
            private Vector4[] _weights;
            private byte[] _indices; // four per vertex

            public static SkinSource For(VertexBuffer buffer)
            {
                lock (Cache)
                {
                    if (Cache.TryGetValue(buffer, out var cached)) return cached;
                    SkinSource source = Create(buffer);
                    if (source != null) Cache.Add(buffer, source);
                    return source;
                }
            }

            private static SkinSource Create(VertexBuffer buffer)
            {
                VertexDeclaration declaration = buffer.VertexDeclaration;
                int position = -1, indices = -1, weights = -1;
                var source = new SkinSource { VertexCount = buffer.VertexCount, _stride = declaration.VertexStride };
                foreach (VertexElement element in declaration.GetVertexElements())
                {
                    if (element.UsageIndex != 0) continue;
                    bool vector3 = element.VertexElementFormat == VertexElementFormat.Vector3;
                    switch (element.VertexElementUsage)
                    {
                        case VertexElementUsage.Position when vector3: position = element.Offset; break;
                        case VertexElementUsage.Normal when vector3: source._normalOffset = element.Offset; break;
                        case VertexElementUsage.Tangent when vector3: source._tangentOffset = element.Offset; break;
                        case VertexElementUsage.Binormal when vector3: source._binormalOffset = element.Offset; break;
                        case VertexElementUsage.BlendIndices when element.VertexElementFormat == VertexElementFormat.Byte4: indices = element.Offset; break;
                        case VertexElementUsage.BlendWeight when element.VertexElementFormat == VertexElementFormat.Vector4: weights = element.Offset; break;
                    }
                }
                if (position < 0 || indices < 0 || weights < 0) return null;
                source._positionOffset = position;

                byte[] data = new byte[source.VertexCount * source._stride];
                buffer.GetData(0, data, 0, data.Length, 1);
                source.Template = data;

                int count = source.VertexCount, stride = source._stride;
                source._positions = Read<Vector3>(data, count, stride, position);
                source._normals = source._normalOffset >= 0 ? Read<Vector3>(data, count, stride, source._normalOffset) : null;
                source._tangents = source._tangentOffset >= 0 ? Read<Vector3>(data, count, stride, source._tangentOffset) : null;
                source._binormals = source._binormalOffset >= 0 ? Read<Vector3>(data, count, stride, source._binormalOffset) : null;
                source._weights = Read<Vector4>(data, count, stride, weights);
                for (int v = 0; v < count; v++) source._weights[v] = Clean(source._weights[v]);
                source._indices = new byte[count * 4];
                for (int v = 0; v < count; v++)
                    Buffer.BlockCopy(data, v * stride + indices, source._indices, v * 4, 4);
                return source;
            }

            // Drops negligible influences (FBX weights can hold denormals, which make the
            // blend many times slower) and renormalises the rest to sum to one.
            private static Vector4 Clean(Vector4 w)
            {
                const float min = 1e-4f;
                if (w.X < min) w.X = 0;
                if (w.Y < min) w.Y = 0;
                if (w.Z < min) w.Z = 0;
                if (w.W < min) w.W = 0;
                float sum = w.X + w.Y + w.Z + w.W;
                return sum > 0 ? w / sum : Vector4.Zero;
            }

            private static T[] Read<T>(byte[] data, int count, int stride, int offset) where T : unmanaged
            {
                var values = new T[count];
                for (int v = 0; v < count; v++)
                    values[v] = Unsafe.ReadUnaligned<T>(ref data[v * stride + offset]);
                return values;
            }

            public void AppendDominant(List<Vector3> positions, List<int> bones)
            {
                for (int v = 0; v < VertexCount; v++)
                {
                    Vector4 w = _weights[v];
                    int slot = 0;
                    float best = w.X;
                    if (w.Y > best) { best = w.Y; slot = 1; }
                    if (w.Z > best) { best = w.Z; slot = 2; }
                    if (w.W > best) { best = w.W; slot = 3; }
                    if (best <= 0) continue;
                    positions.Add(_positions[v]);
                    bones.Add(_indices[v * 4 + slot]);
                }
            }

            public void Skin(int v, Matrix[] skin, byte[] output)
            {
                Vector4 w = _weights[v];
                if (w.X + w.Y + w.Z + w.W <= 0) return; // unweighted: keeps its bind position
                int i = v * 4;

                // Weighted sum of the bone matrices' affine part (the last column is always 0,0,0,1).
                var m = new Affine();
                m.Add(ref skin[_indices[i]], w.X);
                if (w.Y != 0) m.Add(ref skin[_indices[i + 1]], w.Y);
                if (w.Z != 0) m.Add(ref skin[_indices[i + 2]], w.Z);
                if (w.W != 0) m.Add(ref skin[_indices[i + 3]], w.W);

                int vertex = v * _stride;
                Unsafe.WriteUnaligned(ref output[vertex + _positionOffset], m.TransformPoint(_positions[v]));
                if (_normals != null) Unsafe.WriteUnaligned(ref output[vertex + _normalOffset], m.TransformDirection(_normals[v]));
                if (_tangents != null) Unsafe.WriteUnaligned(ref output[vertex + _tangentOffset], m.TransformDirection(_tangents[v]));
                if (_binormals != null) Unsafe.WriteUnaligned(ref output[vertex + _binormalOffset], m.TransformDirection(_binormals[v]));
            }
        }

        private struct Affine
        {
            private float _m11, _m12, _m13, _m21, _m22, _m23, _m31, _m32, _m33, _m41, _m42, _m43;

            public void Add(ref Matrix b, float weight)
            {
                _m11 += b.M11 * weight; _m12 += b.M12 * weight; _m13 += b.M13 * weight;
                _m21 += b.M21 * weight; _m22 += b.M22 * weight; _m23 += b.M23 * weight;
                _m31 += b.M31 * weight; _m32 += b.M32 * weight; _m33 += b.M33 * weight;
                _m41 += b.M41 * weight; _m42 += b.M42 * weight; _m43 += b.M43 * weight;
            }

            public Vector3 TransformPoint(Vector3 p) => new(
                p.X * _m11 + p.Y * _m21 + p.Z * _m31 + _m41,
                p.X * _m12 + p.Y * _m22 + p.Z * _m32 + _m42,
                p.X * _m13 + p.Y * _m23 + p.Z * _m33 + _m43);

            /// <summary>Rotated/scaled and renormalised (bones may scale; blending shortens directions).</summary>
            public Vector3 TransformDirection(Vector3 d)
            {
                var r = new Vector3(
                    d.X * _m11 + d.Y * _m21 + d.Z * _m31,
                    d.X * _m12 + d.Y * _m22 + d.Z * _m32,
                    d.X * _m13 + d.Y * _m23 + d.Z * _m33);
                float lengthSquared = r.LengthSquared();
                return lengthSquared > 1e-16f ? r * (1f / MathF.Sqrt(lengthSquared)) : r;
            }
        }
    }
}
