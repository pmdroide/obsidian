using System;
using Engine.Entities;
using Engine.Editor;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Renderer.Lighting
{
    /// <summary>
    /// GPU copy of a scene's <see cref="LightmapData"/>: the pages as textures, each model's lightmap geometry (positions
    /// split at chart borders, 32-bit indices) and each gameobject's lightmap UVs as a second vertex stream. Owned by
    /// <see cref="LightingSystem"/>, created and drawn on the game thread.
    /// </summary>
    public sealed class LightmapResources : IDisposable
    {
        public struct PositionVertex : IVertexType
        {
            public Vector3 Position;
            public static readonly VertexDeclaration Declaration =
                new VertexDeclaration(new VertexElement(0, VertexElementFormat.Vector3, VertexElementUsage.Position, 0));
            VertexDeclaration IVertexType.VertexDeclaration => Declaration;
        }

        public struct UvVertex : IVertexType
        {
            public Vector2 Uv;
            //TEXCOORD1: Lightmap.fx reads the lightmap UV as the second texture coordinate
            public static readonly VertexDeclaration Declaration =
                new VertexDeclaration(new VertexElement(0, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 1));
            VertexDeclaration IVertexType.VertexDeclaration => Declaration;
        }

        public sealed class PartGpu
        {
            public VertexBuffer Positions;
            public IndexBuffer Indices;
            public int PrimitiveCount;
            public int SourceVertexCount;
        }

        public sealed class MeshGpu
        {
            public PartGpu[] Parts;
            public Vector3 Center;
            public float Radius;
        }

        public sealed class InstanceGpu
        {
            public LightmapData.Instance Data;
            public VertexBuffer[] Uv;
            //Model the instance was last checked against (vertex counts match the bake)
            public Model CheckedModel;
            public bool Matches;
        }

        public readonly LightmapData Data;
        public readonly Texture2D[] Irradiance, Direction, Mask;
        public readonly MeshGpu[] Meshes;
        private readonly System.Collections.Generic.Dictionary<int, InstanceGpu> _instances = new System.Collections.Generic.Dictionary<int, InstanceGpu>();

        public LightmapResources(GraphicsDevice device, LightmapData data)
        {
            Data = data;
            int pages = data.Pages.Count;
            Irradiance = new Texture2D[pages];
            Direction = new Texture2D[pages];
            Mask = new Texture2D[pages];
            try
            {
                for (int p = 0; p < pages; p++)
                {
                    LightmapData.Page page = data.Pages[p];
                    Irradiance[p] = new Texture2D(device, page.Width, page.Height, false, SurfaceFormat.HalfVector4);
                    Irradiance[p].SetData(page.Irradiance);
                    Direction[p] = new Texture2D(device, page.Width, page.Height, false, SurfaceFormat.Color);
                    Direction[p].SetData(page.Direction);
                    Mask[p] = new Texture2D(device, page.Width, page.Height, false, SurfaceFormat.Color);
                    Mask[p].SetData(page.Mask);
                }

                Meshes = new MeshGpu[data.Meshes.Count];
                for (int m = 0; m < data.Meshes.Count; m++)
                {
                    LightmapData.Mesh mesh = data.Meshes[m];
                    var gpu = new MeshGpu { Parts = new PartGpu[mesh.Parts.Count], Center = mesh.Center, Radius = mesh.Radius };
                    for (int i = 0; i < mesh.Parts.Count; i++)
                    {
                        LightmapUnwrap.Part part = mesh.Parts[i];
                        var partGpu = new PartGpu { SourceVertexCount = part.SourceVertexCount };
                        if (part.Lightmapped && part.Positions.Length > 0 && part.Indices.Length >= 3)
                        {
                            var vertices = new PositionVertex[part.Positions.Length];
                            for (int v = 0; v < vertices.Length; v++) vertices[v].Position = part.Positions[v];
                            partGpu.Positions = new VertexBuffer(device, PositionVertex.Declaration, vertices.Length, BufferUsage.WriteOnly);
                            partGpu.Positions.SetData(vertices);
                            partGpu.Indices = new IndexBuffer(device, IndexElementSize.ThirtyTwoBits, part.Indices.Length, BufferUsage.WriteOnly);
                            partGpu.Indices.SetData(part.Indices);
                            partGpu.PrimitiveCount = part.Indices.Length / 3;
                        }
                        gpu.Parts[i] = partGpu;
                    }
                    Meshes[m] = gpu;
                }

                foreach (LightmapData.Instance instance in data.Instances)
                {
                    var gpu = new InstanceGpu { Data = instance, Uv = new VertexBuffer[instance.Uv.Length] };
                    for (int p = 0; p < instance.Uv.Length; p++)
                    {
                        Vector2[] uv = instance.Uv[p];
                        if (uv == null || uv.Length == 0) continue;
                        var vertices = new UvVertex[uv.Length];
                        for (int v = 0; v < uv.Length; v++) vertices[v].Uv = uv[v];
                        gpu.Uv[p] = new VertexBuffer(device, UvVertex.Declaration, vertices.Length, BufferUsage.WriteOnly);
                        gpu.Uv[p].SetData(vertices);
                    }
                    _instances[instance.EntityId] = gpu;
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        /// <summary>
        /// The lightmap of a gameobject, or null when it has none or its model isn't the one baked (a different model or
        /// vertex count since the bake).
        /// </summary>
        public InstanceGpu Find(BasicEntity entity)
        {
            if (entity?.Model == null || !_instances.TryGetValue(entity.Id, out InstanceGpu instance)) return null;
            if (!ReferenceEquals(instance.CheckedModel, entity.Model))
            {
                instance.CheckedModel = entity.Model;
                instance.Matches = Matches(entity.Model, Meshes[instance.Data.Mesh], instance);
                if (!instance.Matches)
                    EditorBridge.Log($"Lightmaps: '{entity.Name}' changed model since the bake; it uses the probes until the next bake");
            }
            return instance.Matches ? instance : null;
        }

        private static bool Matches(Model model, MeshGpu mesh, InstanceGpu instance)
        {
            int index = 0;
            foreach (ModelMesh modelMesh in model.Meshes)
                foreach (ModelMeshPart part in modelMesh.MeshParts)
                {
                    if (index >= mesh.Parts.Length || mesh.Parts[index].SourceVertexCount != part.NumVertices) return false;
                    index++;
                }
            return index == mesh.Parts.Length && instance.Uv.Length == mesh.Parts.Length;
        }

        public void Dispose()
        {
            foreach (Texture2D[] textures in new[] { Irradiance, Direction, Mask })
                if (textures != null)
                    foreach (Texture2D t in textures) t?.Dispose();
            if (Meshes != null)
                foreach (MeshGpu mesh in Meshes)
                    if (mesh != null)
                        foreach (PartGpu part in mesh.Parts)
                        {
                            part?.Positions?.Dispose();
                            part?.Indices?.Dispose();
                        }
            foreach (InstanceGpu instance in _instances.Values)
                foreach (VertexBuffer uv in instance.Uv) uv?.Dispose();
            _instances.Clear();
        }
    }
}
