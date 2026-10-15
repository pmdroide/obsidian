using System;
using System.Collections.Generic;
using Engine.Entities;
using Engine.Recources;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Renderer.Helper
{
    /// <summary>
    /// Hardware occlusion culling (Source 2 look, Docs/Research/Source2Look phase 7, optional): after the G-buffer, every
    /// mesh instance in view draws its bounding box against the scene's depth inside an <see cref="OcclusionQuery"/>;
    /// the next frame skips the instances whose query saw no pixel (one frame late, so nothing stalls on the GPU).
    /// Only the G-buffer and forward passes skip occluded meshes; shadows and captures never do.
    /// Off unless <see cref="GameSettings.g_occlusionculling"/>.
    /// </summary>
    public sealed class OcclusionCulling : IDisposable
    {
        private sealed class Entry
        {
            public OcclusionQuery Query;
            public bool Pending;
            public int LastFrame;
        }

        private readonly Dictionary<(MeshLibrary, TransformMatrix), Entry> _entries = new Dictionary<(MeshLibrary, TransformMatrix), Entry>();
        private readonly List<(MeshLibrary, TransformMatrix)> _stale = new List<(MeshLibrary, TransformMatrix)>();
        private BasicEffect _effect;
        private VertexBuffer _box;
        private IndexBuffer _boxIndices;
        private BlendState _noColor;
        private int _frame;

        /// <summary>Instances found occluded last frame (for stats).</summary>
        public int Occluded { get; private set; }

        /// <summary>
        /// Reads last frame's results into <see cref="MeshLibrary.Occluded"/> and issues this frame's queries. Call with
        /// the G-buffer's depth bound, before anything else draws into it.
        /// </summary>
        public void Update(GraphicsDevice graphics, MeshMaterialLibrary library, Matrix viewProjection, Vector3 camera)
        {
            if (!GameSettings.g_occlusionculling)
            {
                if (_entries.Count > 0) Clear(library);
                return;
            }
            EnsureResources(graphics);
            _frame++;
            Occluded = 0;

            graphics.SetVertexBuffer(_box);
            graphics.Indices = _boxIndices;
            graphics.BlendState = _noColor;
            graphics.DepthStencilState = DepthStencilState.DepthRead;
            graphics.RasterizerState = RasterizerState.CullNone;
            _effect.View = Matrix.Identity;
            _effect.Projection = Matrix.Identity;

            for (int m = 0; m < library.Index; m++)
            {
                MaterialLibrary materialLibrary = library.MaterialLib[m];
                for (int l = 0; l < materialLibrary.Index; l++)
                {
                    MeshLibrary meshLib = materialLibrary.GetMeshLibrary()[l];
                    TransformMatrix[] transforms = meshLib.GetWorldMatrices();
                    for (int i = 0; i < meshLib.Index; i++)
                    {
                        TransformMatrix transform = transforms[i];
                        meshLib.Occluded[i] = false;
                        if (transform == null || !meshLib.Rendered[i]) continue;
                        BoundingSphere sphere = meshLib.WorldSphere(i);
                        var key = (meshLib, transform);
                        if (!_entries.TryGetValue(key, out Entry entry))
                            _entries[key] = entry = new Entry { Query = new OcclusionQuery(graphics) };
                        entry.LastFrame = _frame;

                        //Last frame's answer
                        if (entry.Pending && entry.Query.IsComplete)
                        {
                            entry.Pending = false;
                            bool inside = Vector3.DistanceSquared(camera, sphere.Center) <= sphere.Radius * sphere.Radius * 1.5f;
                            if (entry.Query.PixelCount == 0 && !inside)
                            {
                                meshLib.Occluded[i] = true;
                                Occluded++;
                            }
                        }

                        //This frame's question: the box around the bounding sphere, a little larger (TAA jitter)
                        if (entry.Pending) continue;
                        _effect.World = Matrix.CreateScale(sphere.Radius * 1.05f) * Matrix.CreateTranslation(sphere.Center) * viewProjection;
                        _effect.CurrentTechnique.Passes[0].Apply();
                        entry.Query.Begin();
                        graphics.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, 12);
                        entry.Query.End();
                        entry.Pending = true;
                    }
                }
            }

            //Queries of instances gone for a while
            _stale.Clear();
            foreach (var pair in _entries)
                if (_frame - pair.Value.LastFrame > 60) _stale.Add(pair.Key);
            foreach (var key in _stale)
            {
                _entries[key].Query.Dispose();
                _entries.Remove(key);
            }
            graphics.BlendState = BlendState.Opaque;
            graphics.DepthStencilState = DepthStencilState.Default;
        }

        private void Clear(MeshMaterialLibrary library)
        {
            foreach (Entry entry in _entries.Values) entry.Query.Dispose();
            _entries.Clear();
            for (int m = 0; m < library.Index; m++)
            {
                MaterialLibrary materialLibrary = library.MaterialLib[m];
                for (int l = 0; l < materialLibrary.Index; l++)
                {
                    MeshLibrary meshLib = materialLibrary.GetMeshLibrary()[l];
                    for (int i = 0; i < meshLib.Index; i++) meshLib.Occluded[i] = false;
                }
            }
        }

        private void EnsureResources(GraphicsDevice graphics)
        {
            if (_effect != null) return;
            _effect = new BasicEffect(graphics) { VertexColorEnabled = false, LightingEnabled = false, TextureEnabled = false };
            var corners = new VertexPosition[8];
            for (int i = 0; i < 8; i++)
                corners[i] = new VertexPosition(new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            _box = new VertexBuffer(graphics, VertexPosition.VertexDeclaration, 8, BufferUsage.WriteOnly);
            _box.SetData(corners);
            short[] indices =
            {
                0, 1, 3, 0, 3, 2, 4, 6, 7, 4, 7, 5, 0, 4, 5, 0, 5, 1,
                2, 3, 7, 2, 7, 6, 0, 2, 6, 0, 6, 4, 1, 5, 7, 1, 7, 3,
            };
            _boxIndices = new IndexBuffer(graphics, IndexElementSize.SixteenBits, indices.Length, BufferUsage.WriteOnly);
            _boxIndices.SetData(indices);
            _noColor = new BlendState { ColorWriteChannels = ColorWriteChannels.None, ColorWriteChannels1 = ColorWriteChannels.None,
                ColorWriteChannels2 = ColorWriteChannels.None, ColorWriteChannels3 = ColorWriteChannels.None };
        }

        public void Dispose()
        {
            foreach (Entry entry in _entries.Values) entry.Query.Dispose();
            _entries.Clear();
            _effect?.Dispose();
            _box?.Dispose();
            _boxIndices?.Dispose();
            _noColor?.Dispose();
        }
    }
}
