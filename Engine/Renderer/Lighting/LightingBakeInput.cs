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
            public float SourceRadius;
            public Vector3 Radiance;
            //Spot cone (see SpotLight.ConeFactor); point lights use CosOuter = -2, CosInner = -1
            public Vector3 SpotDirection;
            public float SpotCosOuter;
            public float SpotCosInner;
        }

        public Vector3[] A, B, C;
        public Vector3[] Normal;  //geometric normal, oriented to agree with the vertex normals
        public Vector3[] Albedo;  //linear, clamped below 1 for energy conservation (the texture's average when textured)
        //Light the surface gives off itself (Emissive materials: linear albedo x Emissive Strength), deferred light units
        public Vector3[] Emission;

        // Per-texel albedo (Source 2 look, phase 4.1): texture coordinates of each corner (tiling applied), the albedo
        // texture (index into Textures, -1 = none) and the sRGB tint it is multiplied by.
        public Vector2[] UvA, UvB, UvC;
        public int[] TextureIndex;
        public Vector3[] Tint;
        public readonly List<AlbedoTexture> Textures = new List<AlbedoTexture>();

        /// <summary>An albedo texture read back from the GPU at a small mip (sRGB colours, wrapped lookups).</summary>
        public sealed class AlbedoTexture
        {
            public int Width, Height;
            public Vector3[] Pixels;

            public Vector3 Sample(Vector2 uv)
            {
                float u = uv.X - MathF.Floor(uv.X), v = uv.Y - MathF.Floor(uv.Y);
                int x = Math.Min((int)(u * Width), Width - 1), y = Math.Min((int)(v * Height), Height - 1);
                return Pixels[y * Width + x];
            }
        }

        /// <summary>
        /// Linear albedo of triangle <paramref name="triangle"/> at the world point <paramref name="p"/> on it: its texture
        /// there (times the tint) when it has one, else <see cref="Albedo"/>.
        /// </summary>
        public Vector3 AlbedoAt(int triangle, Vector3 p)
        {
            int texture = TextureIndex == null ? -1 : TextureIndex[triangle];
            if (texture < 0) return Albedo[triangle];

            //Barycentric coordinates of p
            Vector3 a = A[triangle], e1 = B[triangle] - a, e2 = C[triangle] - a, ep = p - a;
            float d11 = Vector3.Dot(e1, e1), d12 = Vector3.Dot(e1, e2), d22 = Vector3.Dot(e2, e2);
            float dp1 = Vector3.Dot(ep, e1), dp2 = Vector3.Dot(ep, e2);
            float denominator = d11 * d22 - d12 * d12;
            if (Math.Abs(denominator) < 1e-12f) return Albedo[triangle];
            float v = (d22 * dp1 - d12 * dp2) / denominator;
            float w = (d11 * dp2 - d12 * dp1) / denominator;
            float u = 1 - v - w;
            Vector2 uv = UvA[triangle] * u + UvB[triangle] * v + UvC[triangle] * w;

            Vector3 srgb = Textures[texture].Sample(uv) * Tint[triangle];
            return ToLinearAlbedo(srgb);
        }

        private static Vector3 ToLinearAlbedo(Vector3 srgb)
        {
            Vector3 linear = new Vector3(
                (float)Math.Pow(MathHelper.Clamp(srgb.X, 0, 1), 2.2),
                (float)Math.Pow(MathHelper.Clamp(srgb.Y, 0, 1), 2.2),
                (float)Math.Pow(MathHelper.Clamp(srgb.Z, 0, 1), 2.2));
            return Vector3.Min(linear, new Vector3(MaxAlbedo));
        }

        /// <summary>A detail probe volume to bake (Probe Volume component).</summary>
        public struct DetailVolumeInput
        {
            public BoundingBox Bounds;
            public float Spacing;
            public int Priority;
            public float BlendMargin;
        }

        public readonly List<DetailVolumeInput> DetailVolumes = new List<DetailVolumeInput>();

        /// <summary>Emissive triangles (indices), sampled directly as area lights so small bright panels don't turn into noise.</summary>
        public int[] Emitters = Array.Empty<int>();

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
            public Vector2[] TexCoords; //may be null
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
            var emission = new List<Vector3>();
            var uvA = new List<Vector2>();
            var uvB = new List<Vector2>();
            var uvC = new List<Vector2>();
            var textureIndex = new List<int>();
            var tint = new List<Vector3>();
            var textureLookup = new Dictionary<Texture2D, int>();

            var geometryCache = new Dictionary<Model, List<MeshPartGeometry>>();
            var albedoCache = new Dictionary<Texture2D, Vector3?>();
            Vector3 bMin = new Vector3(float.MaxValue), bMax = new Vector3(float.MinValue);

            foreach (BasicEntity entity in scene.BasicEntities)
            {
                //Detail probe volumes are boxes, not geometry
                Components.ProbeVolumeComponent volume = entity?.GetComponent<Components.ProbeVolumeComponent>();
                if (volume != null)
                {
                    if (volume.Enabled && entity.IsEnabled)
                        input.DetailVolumes.Add(new DetailVolumeInput
                        {
                            Bounds = Components.ProbeVolumeComponent.Bounds(entity),
                            Spacing = volume.ClampedSpacing,
                            Priority = volume.Priority,
                            BlendMargin = volume.ClampedMargin,
                        });
                    continue;
                }
                //Reflection probes, post process and fog volumes are boxes too
                if (entity?.GetComponent<Components.ReflectionProbeComponent>() is { Enabled: true } ||
                    entity?.GetComponent<Components.PostProcessVolumeComponent>() is { Enabled: true } ||
                    entity?.GetComponent<Components.FogVolumeComponent>() is { Enabled: true })
                    continue;

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

                //A Material component replaces the model's materials (BasicEntity.RegisterModel)
                Components.MaterialComponent component = null;
                foreach (Components.GameComponent attached in entity.Components)
                    if (attached is Components.MaterialComponent m && m.Enabled) { component = m; break; }

                foreach (MeshPartGeometry part in parts)
                {
                    Surface surface = component != null
                        ? Surface.From(component)
                        : Surface.From(entity.Material ?? part.EmbeddedMaterial);
                    Vector3 partAlbedo = EstimateAlbedo(surface, albedoCache);
                    Vector3 partEmission = surface.EmissiveStrength > 0 && surface.Type == MaterialEffect.MaterialTypes.Emissive
                        ? partAlbedo * Math.Min(surface.EmissiveStrength, 8)
                        : Vector3.Zero;
                    int partTexture = -1;
                    if (surface.Texture != null && part.TexCoords != null)
                    {
                        if (!textureLookup.TryGetValue(surface.Texture, out partTexture))
                        {
                            AlbedoTexture read = ReadTexture(surface.Texture);
                            partTexture = -1;
                            if (read != null)
                            {
                                partTexture = input.Textures.Count;
                                input.Textures.Add(read);
                            }
                            textureLookup[surface.Texture] = partTexture;
                        }
                    }

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
                        emission.Add(partEmission);
                        textureIndex.Add(partTexture);
                        tint.Add(surface.Tint);
                        if (part.TexCoords != null)
                        {
                            uvA.Add(part.TexCoords[i0] * surface.Tiling);
                            uvB.Add(part.TexCoords[i1] * surface.Tiling);
                            uvC.Add(part.TexCoords[i2] * surface.Tiling);
                        }
                        else
                        {
                            uvA.Add(Vector2.Zero); uvB.Add(Vector2.Zero); uvC.Add(Vector2.Zero);
                        }

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
            input.Emission = emission.ToArray();
            var emitters = new List<int>();
            for (int i = 0; i < input.Emission.Length && emitters.Count < 4096; i++)
                if (input.Emission[i].LengthSquared() > 1e-8f) emitters.Add(i);
            input.Emitters = emitters.ToArray();
            input.UvA = uvA.ToArray();
            input.UvB = uvB.ToArray();
            input.UvC = uvC.ToArray();
            input.TextureIndex = textureIndex.ToArray();
            input.Tint = tint.ToArray();
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
                SpotLight spot = pl as SpotLight;
                input.PointLights.Add(new PointLightInput
                {
                    Position = pl.Position,
                    Radius = pl.Radius,
                    SourceRadius = pl.SourceRadius,
                    Radiance = pl.ColorV3 * pl.Intensity,
                    SpotDirection = spot?.Direction ?? Vector3.UnitZ,
                    SpotCosOuter = spot?.CosOuter ?? -2,
                    SpotCosInner = spot?.CosInner ?? -1,
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
            int positionOffset = -1, normalOffset = -1, uvOffset = -1;
            foreach (VertexElement e in decl.GetVertexElements())
            {
                if (e.UsageIndex != 0) continue;
                if (e.VertexElementFormat == VertexElementFormat.Vector2 && e.VertexElementUsage == VertexElementUsage.TextureCoordinate)
                    uvOffset = e.Offset;
                if (e.VertexElementFormat != VertexElementFormat.Vector3) continue;
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
            if (uvOffset >= 0)
            {
                geo.TexCoords = new Vector2[part.NumVertices];
                part.VertexBuffer.GetData(baseOffset + uvOffset, geo.TexCoords, 0, part.NumVertices, stride);
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

        /// <summary>What the bake needs of a surface: the G-buffer's rules (Gbuffer.fx WriteBuffers).</summary>
        private struct Surface
        {
            public Vector3 Color;      //sRGB: the colour without a texture
            public Texture2D Texture;  //albedo texture or null
            public Vector3 Tint;       //sRGB multiplier of the texture (Material components tint, plain materials don't)
            public Vector2 Tiling;
            public float EmissiveStrength;
            public MaterialEffect.MaterialTypes Type;

            public static Surface From(MaterialEffect material)
            {
                if (material == null) return new Surface { Color = new Vector3(0.73f), Tint = Vector3.One, Tiling = Vector2.One };
                return new Surface
                {
                    Color = material.DiffuseColor,
                    Texture = material.HasDiffuse ? material.AlbedoMap : null,
                    Tint = material.IsInstanceMaterial ? material.DiffuseColor : Vector3.One,
                    Tiling = material.TextureTiling,
                    EmissiveStrength = material.EmissiveStrength,
                    Type = material.Type,
                };
            }

            public static Surface From(Components.MaterialComponent component)
            {
                var color = new Vector3(MathHelper.Clamp(component.Red, 0, 1), MathHelper.Clamp(component.Green, 0, 1), MathHelper.Clamp(component.Blue, 0, 1));
                Texture2D texture = null;
                if (!string.IsNullOrEmpty(component.BaseColorTexture) && Globals.content != null &&
                    Components.MaterialComponent.IsTextureAsset(component.BaseColorTexture))
                {
                    try { texture = Globals.content.Load<Texture2D>(Path.ChangeExtension(component.BaseColorTexture.Replace('\\', '/'), null)); }
                    catch (Exception e) { EditorBridge.Log($"LightingBakeInput: albedo '{component.BaseColorTexture}': {e.Message}"); }
                }
                return new Surface
                {
                    Color = color,
                    Texture = texture,
                    Tint = color,
                    Tiling = new Vector2(Math.Max(component.TilingU, 0.001f), Math.Max(component.TilingV, 0.001f)),
                    EmissiveStrength = component.EmissiveStrength,
                    Type = component.MaterialType,
                };
            }
        }

        /// <summary>Linear albedo of a surface: its texture's average (times the tint) when readable, else its colour.</summary>
        private static Vector3 EstimateAlbedo(Surface surface, Dictionary<Texture2D, Vector3?> cache)
        {
            Vector3 srgb = surface.Color;
            if (surface.Texture != null)
            {
                if (!cache.TryGetValue(surface.Texture, out Vector3? average))
                {
                    average = AverageTexture(surface.Texture);
                    cache[surface.Texture] = average;
                }
                if (average.HasValue) srgb = average.Value * surface.Tint;
            }
            return ToLinearAlbedo(srgb);
        }

        /// <summary>The texture at its first mip of at most 256 texels a side, for per-texel albedo. Null for compressed formats.</summary>
        private static AlbedoTexture ReadTexture(Texture2D texture)
        {
            if (texture.Format != SurfaceFormat.Color) return null;
            try
            {
                int level = 0;
                while (level < texture.LevelCount - 1 && Math.Max(texture.Width >> level, texture.Height >> level) > 256)
                    level++;
                int w = Math.Max(1, texture.Width >> level), h = Math.Max(1, texture.Height >> level);
                var pixels = new Color[w * h];
                texture.GetData(level, null, pixels, 0, pixels.Length);
                var result = new AlbedoTexture { Width = w, Height = h, Pixels = new Vector3[pixels.Length] };
                for (int i = 0; i < pixels.Length; i++) result.Pixels[i] = pixels[i].ToVector3();
                return result;
            }
            catch (Exception e)
            {
                EditorBridge.Log($"LightingBakeInput: couldn't read albedo '{texture.Name}': {e.Message}");
                return null;
            }
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
