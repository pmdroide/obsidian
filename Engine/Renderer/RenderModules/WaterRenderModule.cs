using Engine.Entities;
using Engine.Recources;
using Engine.Renderer.Helper;
using Engine.Renderer.RenderModules.Default;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using DirectionalLight = Engine.Entities.DirectionalLight;

namespace Engine.Renderer.RenderModules;

public sealed class WaterRenderModule : IRenderModule, IDisposable
{
    // Triangle budget for one subdivided water mesh part: a two-triangle plane becomes a 256x256 grid.
    private const int MaxTessellatedTriangles = 131072;

    private readonly Effect _shader;
    private readonly Dictionary<ModelMeshPart, TessellatedMesh> _meshes = new(ReferenceEqualityComparer.Instance);
    // Model-space vertex spacing of the mesh bound by the last BindMesh call.
    private float _meshSpacing = 1;

    /// <summary>
    /// A subdivided copy of a mesh part, so the vertex shader has vertices to move with the waves.
    /// No buffers when the part is already dense enough to draw as it is.
    /// </summary>
    private sealed class TessellatedMesh : IDisposable
    {
        public VertexBuffer Vertices;
        public IndexBuffer Indices;
        public int PrimitiveCount;
        public float Spacing;

        public void Dispose()
        {
            Vertices?.Dispose();
            Indices?.Dispose();
        }
    }

    public WaterRenderModule(ContentManager content) =>
        _shader = content.Load<Effect>("Shaders/Forward/Water");

    /// <param name="depthMap">Linear G-buffer depth (view Z / -farClip); enables shore depth, absorption and foam.</param>
    public void Draw(GraphicsDevice graphics, MeshMaterialLibrary meshes, Matrix viewProjection,
        Camera camera, TextureCube environment, List<DirectionalLight> lights, GameTime time,
        Texture2D depthMap = null, Matrix? view = null, float farClip = 0)
    {
        _shader.Parameters["CameraPositionWS"].SetValue(camera.Position);
        // Buoyancy samples the same clock (ScreenManager.UpdatePhysics), so floating bodies ride these waves.
        _shader.Parameters["Time"].SetValue((float)time.TotalGameTime.TotalSeconds);
        _shader.Parameters["HasEnvironment"].SetValue(environment != null);
        _shader.Parameters["EnvironmentMap"].SetValue(environment);
        bool hasDepth = depthMap != null && view.HasValue && farClip > 0;
        _shader.Parameters["HasDepth"].SetValue(hasDepth);
        _shader.Parameters["DepthMap"].SetValue(hasDepth ? depthMap : null);
        _shader.Parameters["View"].SetValue(view ?? Matrix.Identity);
        _shader.Parameters["FarClip"].SetValue(farClip);
        var light = lights.FirstOrDefault(l => l.IsEnabled && l.Intensity > 0);
        _shader.Parameters["LightDirection"].SetValue(light == null ? Vector3.UnitZ : -light.Direction);
        // Same 0.1 scale the deferred light shaders apply, so water matches the lit scene.
        _shader.Parameters["LightColor"].SetValue(light == null ? Vector3.Zero : light.ColorV3 * light.Intensity * 0.1f);
        // Sort water instances even when CPU culling/sorting is disabled.
        for (int i = 0; i < meshes.Index; i++)
        {
            var batch = meshes.MaterialLib[i];
            if (batch.GetMaterial().Type != MaterialEffect.MaterialTypes.Water || batch.Index == 0) continue;
            batch.DistanceSquared = Vector3.DistanceSquared(camera.Position,
                batch.GetMeshLibrary()[0].GetWorldMatrices()[0].World.Translation);
        }
        meshes.Draw(MeshMaterialLibrary.RenderType.Water, viewProjection, renderModule: this);
        _shader.Parameters["DepthMap"].SetValue((Texture2D)null);
        graphics.DepthStencilState = DepthStencilState.Default;
        graphics.RasterizerState = RasterizerState.CullCounterClockwise;
        graphics.BlendState = BlendState.Opaque;
    }

    public void SetMaterialSettings(MaterialEffect material)
    {
        _shader.Parameters["SurfaceColor"].SetValue(material.DiffuseColor);
        _shader.Parameters["Roughness"].SetValue(material.Roughness);
        _shader.Parameters["Opacity"].SetValue(material.Opacity);
        _shader.Parameters["WaveScale"].SetValue(material.WaveScale);
        _shader.Parameters["WaveSpeed"].SetValue(material.WaveSpeed);
        _shader.Parameters["WaveStrength"].SetValue(material.WaveStrength);
        _shader.Parameters["WaveHeight"].SetValue(material.WaveHeight);
        _shader.Parameters["Clarity"].SetValue(material.WaterClarity);
        _shader.Parameters["Foam"].SetValue(material.WaterFoam);
    }

    /// <summary>
    /// Bind the subdivided copy of <paramref name="part"/> in place of its own buffers. Returns false when
    /// the caller should draw the part's own buffers: it is already dense, or its data can't be read back
    /// (then the swell is switched off for it, as the mesh has too few vertices to show it).
    /// </summary>
    public bool BindMesh(GraphicsDevice graphics, ModelMeshPart part, out int primitiveCount)
    {
        if (!_meshes.TryGetValue(part, out TessellatedMesh mesh))
        {
            try { mesh = Tessellate(graphics, part); }
            catch (Exception ex)
            {
                Editor.EditorBridge.Log($"Water: couldn't subdivide a mesh for waves, drawing it flat: {ex.Message}");
                mesh = null;
            }
            _meshes[part] = mesh;
        }

        primitiveCount = 0;
        _meshSpacing = mesh?.Spacing ?? 1e6f;
        if (mesh?.Vertices == null) return false;
        graphics.SetVertexBuffer(mesh.Vertices);
        graphics.Indices = mesh.Indices;
        primitiveCount = mesh.PrimitiveCount;
        return true;
    }

    public void Apply(Matrix world, Matrix? view, Matrix viewProjection)
    {
        _shader.Parameters["World"].SetValue(world);
        _shader.Parameters["ViewProj"].SetValue(viewProjection);
        _shader.Parameters["WorldInverseTranspose"].SetValue(Matrix.Transpose(Matrix.Invert(world)));
        float scale = Math.Max(world.Right.Length(), Math.Max(world.Forward.Length(), world.Up.Length()));
        _shader.Parameters["GridSpacing"].SetValue(_meshSpacing * scale);
        _shader.CurrentTechnique.Passes[0].Apply();
    }

    /// <summary>
    /// Split every triangle into n*n smaller ones. One n for the whole part keeps shared edges split
    /// identically, so the displaced surface has no cracks.
    /// </summary>
    private static TessellatedMesh Tessellate(GraphicsDevice graphics, ModelMeshPart part)
    {
        VertexDeclaration declaration = part.VertexBuffer.VertexDeclaration;
        int stride = declaration.VertexStride;
        int positionOffset = 0, normalOffset = -1;
        foreach (VertexElement element in declaration.GetVertexElements())
        {
            if (element.UsageIndex != 0) continue;
            if (element.VertexElementUsage == VertexElementUsage.Position) positionOffset = element.Offset;
            else if (element.VertexElementUsage == VertexElementUsage.Normal) normalOffset = element.Offset;
        }

        var bytes = new byte[part.NumVertices * stride];
        part.VertexBuffer.GetData(part.VertexOffset * stride, bytes, 0, bytes.Length);
        var positions = new Vector3[part.NumVertices];
        var normals = new Vector3[part.NumVertices];
        for (int i = 0; i < part.NumVertices; i++)
        {
            positions[i] = ReadVector3(bytes, i * stride + positionOffset);
            normals[i] = normalOffset >= 0 ? ReadVector3(bytes, i * stride + normalOffset) : Vector3.UnitZ;
        }

        int indexCount = part.PrimitiveCount * 3;
        var sourceIndices = new int[indexCount];
        if (part.IndexBuffer.IndexElementSize == IndexElementSize.ThirtyTwoBits)
            part.IndexBuffer.GetData(part.StartIndex * 4, sourceIndices, 0, indexCount);
        else
        {
            var shortIndices = new ushort[indexCount];
            part.IndexBuffer.GetData(part.StartIndex * 2, shortIndices, 0, indexCount);
            for (int i = 0; i < indexCount; i++) sourceIndices[i] = shortIndices[i];
        }

        int n = Math.Max(1, (int)Math.Sqrt(MaxTessellatedTriangles / (double)Math.Max(part.PrimitiveCount, 1)));
        if (n == 1)
        {
            double sourceArea = 0;
            for (int t = 0; t < part.PrimitiveCount; t++)
            {
                Vector3 a = positions[sourceIndices[t * 3]];
                sourceArea += Vector3.Cross(positions[sourceIndices[t * 3 + 1]] - a, positions[sourceIndices[t * 3 + 2]] - a).Length() * 0.5;
            }
            return new TessellatedMesh { Spacing = (float)Math.Sqrt(2 * sourceArea / Math.Max(part.PrimitiveCount, 1)) };
        }
        int verticesPerTriangle = (n + 1) * (n + 2) / 2;
        var vertices = new VertexPositionNormalTexture[part.PrimitiveCount * verticesPerTriangle];
        var indices = new int[part.PrimitiveCount * n * n * 3];
        int vertex = 0, index = 0;
        double area = 0;

        for (int t = 0; t < part.PrimitiveCount; t++)
        {
            int i0 = sourceIndices[t * 3], i1 = sourceIndices[t * 3 + 1], i2 = sourceIndices[t * 3 + 2];
            Vector3 p0 = positions[i0], p1 = positions[i1], p2 = positions[i2];
            Vector3 n0 = normals[i0], n1 = normals[i1], n2 = normals[i2];
            area += Vector3.Cross(p1 - p0, p2 - p0).Length() * 0.5;

            // Row r runs from p0 towards p2, column c towards p1.
            int first = vertex;
            for (int r = 0; r <= n; r++)
            for (int c = 0; c <= n - r; c++)
            {
                float u = c / (float)n, v = r / (float)n, w = 1 - u - v;
                Vector3 normal = n0 * w + n1 * u + n2 * v;
                vertices[vertex++] = new VertexPositionNormalTexture(p0 * w + p1 * u + p2 * v,
                    normal.LengthSquared() > 0 ? Vector3.Normalize(normal) : Vector3.UnitZ, Vector2.Zero);
            }

            int At(int r, int c) => first + r * (n + 1) - r * (r - 1) / 2 + c;
            for (int r = 0; r < n; r++)
            for (int c = 0; c < n - r; c++)
            {
                // Same winding as the source triangle (p0, p1, p2).
                indices[index++] = At(r, c);
                indices[index++] = At(r, c + 1);
                indices[index++] = At(r + 1, c);
                if (c < n - r - 1)
                {
                    indices[index++] = At(r, c + 1);
                    indices[index++] = At(r + 1, c + 1);
                    indices[index++] = At(r + 1, c);
                }
            }
        }

        var mesh = new TessellatedMesh
        {
            Vertices = new VertexBuffer(graphics, VertexPositionNormalTexture.VertexDeclaration, vertices.Length, BufferUsage.WriteOnly),
            PrimitiveCount = index / 3,
            // Leg of a right triangle with the average small-triangle area.
            Spacing = (float)Math.Sqrt(2 * area / Math.Max(index / 3, 1)),
        };
        mesh.Vertices.SetData(vertices);
        if (vertices.Length <= ushort.MaxValue)
        {
            mesh.Indices = new IndexBuffer(graphics, IndexElementSize.SixteenBits, index, BufferUsage.WriteOnly);
            mesh.Indices.SetData(Array.ConvertAll(indices, i => (ushort)i));
        }
        else
        {
            mesh.Indices = new IndexBuffer(graphics, IndexElementSize.ThirtyTwoBits, index, BufferUsage.WriteOnly);
            mesh.Indices.SetData(indices);
        }
        return mesh;
    }

    private static Vector3 ReadVector3(byte[] bytes, int offset) => new Vector3(
        BitConverter.ToSingle(bytes, offset), BitConverter.ToSingle(bytes, offset + 4), BitConverter.ToSingle(bytes, offset + 8));

    public void Dispose()
    {
        foreach (var mesh in _meshes.Values) mesh?.Dispose();
        _meshes.Clear();
        _shader.Dispose();
    }
}
