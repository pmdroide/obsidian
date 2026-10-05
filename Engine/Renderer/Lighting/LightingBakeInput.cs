using System;
using System.Collections.Generic;
using System.IO;
using Engine.Editor;
using Engine.Entities;
using Engine.Logic;
using Engine.Recources;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DirectionalLight = Engine.Entities.DirectionalLight;

namespace Engine.Renderer.Lighting
{
    /// <summary>
    /// Self-contained world-space snapshot of everything a bake needs: a triangle soup with a
    /// per-triangle albedo and outward normal, plus the enabled lights. Gathered on the game
    /// thread (vertex/index/texture readback touches the GraphicsDevice); the worker thread
    /// then bakes from this copy without touching live scene state.
    /// </summary>
    public sealed class LightingBakeInput
    {
        public struct DirectionalLightInput
        {
            public Vector3 ToLight;   //normalized, from surface towards the light
            public Vector3 Radiance;  //linear colour * intensity
        }

        public struct PointLightInput
        {
            public Vector3 Position;
            public float Radius;
            public Vector3 Radiance;
        }

        public Vector3[] A, B, C;
        public Vector3[] Normal;  //geometric normal, oriented to agree with the vertex normals
        public Vector3[] Albedo;  //linear, clamped below 1 for energy conservation

        public readonly List<DirectionalLightInput> DirectionalLights = new List<DirectionalLightInput>();
        public readonly List<PointLightInput> PointLights = new List<PointLightInput>();

        public BoundingBox GeometryBounds;
        public int EntityCount;

        public int TriangleCount => A?.Length ?? 0;

        private static readonly Vector3 DefaultAlbedo = new Vector3(0.5f);
        private const float MaxAlbedo = 0.9f;

        //Model-space geometry is shared between instances of the same model
        private sealed class MeshPartGeometry
        {
            public Vector3[] Positions;
            public Vector3[] Normals; //may be null when the vertex format has none
            public int[] Indices;
            public MaterialEffect EmbeddedMaterial;
        }

        public static LightingBakeInput Gather(Scene scene)
        {
            var input = new LightingBakeInput();
            var a = new List<Vector3>();
            var b = new List<Vector3>();
            var c = new List<Vector3>();
            var normals = new List<Vector3>();
            var albedo = new List<Vector3>();

            var geometryCache = new Dictionary<Model, List<MeshPartGeometry>>();
            var albedoCache = new Dictionary<Texture2D, Vector3?>();
            Vector3 bMin = new Vector3(float.MaxValue), bMax = new Vector3(float.MinValue);

            foreach (BasicEntity entity in scene.BasicEntities)
            {
                //IsEnabled is not consulted: the renderer draws BasicEntities regardless of it,
                //so bake exactly what is drawn.
                if (entity == null || entity.Model == null) continue;

                if (!geometryCache.TryGetValue(entity.Model, out List<MeshPartGeometry> parts))
                {
                    parts = ExtractGeometry(entity.Model);
                    geometryCache[entity.Model] = parts;
                }

                //Same composition as BasicEntity.ApplyTransformation (static path)
                Matrix world = Matrix.CreateScale(entity.Scale) * entity.RotationMatrix * Matrix.CreateTranslation(entity.Position);
                Matrix normalMatrix = Matrix.Transpose(Matrix.Invert(world));

                foreach (MeshPartGeometry part in parts)
                {
                    Vector3 partAlbedo = EstimateAlbedo(entity.Material ?? part.EmbeddedMaterial, albedoCache);

                    for (int i = 0; i + 2 < part.Indices.Length; i += 3)
                    {
                        int i0 = part.Indices[i], i1 = part.Indices[i + 1], i2 = part.Indices[i + 2];
                        Vector3 p0 = Vector3.Transform(part.Positions[i0], world);
                        Vector3 p1 = Vector3.Transform(part.Positions[i1], world);
                        Vector3 p2 = Vector3.Transform(part.Positions[i2], world);

                        Vector3 n = Vector3.Cross(p1 - p0, p2 - p0);
                        float len = n.Length();
                        if (len < 1e-12f) continue; //degenerate
                        n /= len;

                        //Winding conventions vary between exporters; trust the authored normals
                        if (part.Normals != null)
                        {
                            Vector3 vn = Vector3.TransformNormal(part.Normals[i0] + part.Normals[i1] + part.Normals[i2], normalMatrix);
                            if (Vector3.Dot(vn, n) < 0) n = -n;
                        }

                        a.Add(p0); b.Add(p1); c.Add(p2);
                        normals.Add(n);
                        albedo.Add(partAlbedo);

                        bMin = Vector3.Min(bMin, Vector3.Min(p0, Vector3.Min(p1, p2)));
                        bMax = Vector3.Max(bMax, Vector3.Max(p0, Vector3.Max(p1, p2)));
                    }
                }
                input.EntityCount++;
            }

            input.A = a.ToArray();
            input.B = b.ToArray();
            input.C = c.ToArray();
            input.Normal = normals.ToArray();
            input.Albedo = albedo.ToArray();
            input.GeometryBounds = input.TriangleCount > 0 ? new BoundingBox(bMin, bMax) : new BoundingBox();

            foreach (DirectionalLight dl in scene.DirectionalLights)
            {
                if (dl == null || !dl.IsEnabled || dl.Intensity <= 0) continue;
                Vector3 dir = dl.Direction;
                if (dir.LengthSquared() < 1e-8f) continue;
                input.DirectionalLights.Add(new DirectionalLightInput
                {
                    //The deferred shader lights with -Direction
                    ToLight = -Vector3.Normalize(dir),
                    Radiance = dl.ColorV3 * dl.Intensity,
                });
            }

            foreach (PointLight pl in scene.PointLights)
            {
                if (pl == null || !pl.IsEnabled || pl.Intensity <= 0 || pl.Radius <= 0) continue;
                input.PointLights.Add(new PointLightInput
                {
                    Position = pl.Position,
                    Radius = pl.Radius,
                    Radiance = pl.ColorV3 * pl.Intensity,
                });
            }

            return input;
        }

        private static List<MeshPartGeometry> ExtractGeometry(Model model)
        {
            var result = new List<MeshPartGeometry>();
            //Bone transforms are ignored, matching MeshMaterialLibrary and the SDF generator.
            foreach (ModelMesh mesh in model.Meshes)
            {
                foreach (ModelMeshPart part in mesh.MeshParts)
                {
                    try
                    {
                        result.Add(ExtractPart(part));
                    }
                    catch (Exception e)
                    {
                        EditorBridge.Log($"LightingBakeInput: skipped a mesh part of '{mesh.Name}': {e.Message}");
                    }
                }
            }
            return result;
        }

        private static MeshPartGeometry ExtractPart(ModelMeshPart part)
        {
            VertexDeclaration decl = part.VertexBuffer.VertexDeclaration;
            int stride = decl.VertexStride;
            int positionOffset = -1, normalOffset = -1;
            foreach (VertexElement e in decl.GetVertexElements())
            {
                if (e.VertexElementFormat != VertexElementFormat.Vector3 || e.UsageIndex != 0) continue;
                if (e.VertexElementUsage == VertexElementUsage.Position) positionOffset = e.Offset;
                else if (e.VertexElementUsage == VertexElementUsage.Normal) normalOffset = e.Offset;
            }
            if (positionOffset < 0) throw new InvalidOperationException("vertex format has no Vector3 position");

            var geo = new MeshPartGeometry
            {
                Positions = new Vector3[part.NumVertices],
                EmbeddedMaterial = part.Effect as MaterialEffect,
            };
            int baseOffset = part.VertexOffset * stride;
            part.VertexBuffer.GetData(baseOffset + positionOffset, geo.Positions, 0, part.NumVertices, stride);
            if (normalOffset >= 0)
            {
                geo.Normals = new Vector3[part.NumVertices];
                part.VertexBuffer.GetData(baseOffset + normalOffset, geo.Normals, 0, part.NumVertices, stride);
            }

            int indexCount = part.PrimitiveCount * 3;
            geo.Indices = new int[indexCount];
            if (part.IndexBuffer.IndexElementSize == IndexElementSize.ThirtyTwoBits)
            {
                part.IndexBuffer.GetData(part.StartIndex * 4, geo.Indices, 0, indexCount);
            }
            else
            {
                var shortIndices = new ushort[indexCount];
                part.IndexBuffer.GetData(part.StartIndex * 2, shortIndices, 0, indexCount);
                for (int i = 0; i < indexCount; i++) geo.Indices[i] = shortIndices[i];
            }

            //Indices are relative to VertexOffset (DrawIndexedPrimitives baseVertex), as is our copy
            for (int i = 0; i < indexCount; i++)
                if ((uint)geo.Indices[i] >= (uint)part.NumVertices) throw new InvalidDataException("index out of range");

            return geo;
        }

        /// <summary>
        /// Linear albedo for a material: the average of its albedo texture when readable, else its
        /// diffuse colour. Both are stored gamma-encoded (DeferredCompose linearises with pow 2.2).
        /// TODO: per-texel albedo via UVs and emissive surfaces as light sources.
        /// </summary>
        private static Vector3 EstimateAlbedo(MaterialEffect material, Dictionary<Texture2D, Vector3?> cache)
        {
            if (material == null) return DefaultAlbedo;

            Vector3 srgb = material.DiffuseColor;
            if (material.HasDiffuse && material.AlbedoMap != null)
            {
                if (!cache.TryGetValue(material.AlbedoMap, out Vector3? average))
                {
                    average = AverageTexture(material.AlbedoMap);
                    cache[material.AlbedoMap] = average;
                }
                if (average.HasValue) srgb = average.Value;
            }

            Vector3 linear = new Vector3(
                (float)Math.Pow(MathHelper.Clamp(srgb.X, 0, 1), 2.2),
                (float)Math.Pow(MathHelper.Clamp(srgb.Y, 0, 1), 2.2),
                (float)Math.Pow(MathHelper.Clamp(srgb.Z, 0, 1), 2.2));
            return Vector3.Min(linear, new Vector3(MaxAlbedo));
        }

        //Average colour from a small mip. Null for formats we can't read back (e.g. DXT).
        private static Vector3? AverageTexture(Texture2D texture)
        {
            if (texture.Format != SurfaceFormat.Color) return null;
            try
            {
                int level = 0;
                while (level < texture.LevelCount - 1 && Math.Max(texture.Width >> level, texture.Height >> level) > 32)
                    level++;
                int w = Math.Max(1, texture.Width >> level), h = Math.Max(1, texture.Height >> level);
                var pixels = new Color[w * h];
                texture.GetData(level, null, pixels, 0, pixels.Length);

                Vector3 sum = Vector3.Zero;
                foreach (Color p in pixels) sum += p.ToVector3();
                return sum / pixels.Length;
            }
            catch (Exception e)
            {
                EditorBridge.Log($"LightingBakeInput: couldn't read albedo '{texture.Name}': {e.Message}");
                return null;
            }
        }
    }
}
