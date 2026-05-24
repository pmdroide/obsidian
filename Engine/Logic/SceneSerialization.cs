using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Engine.Editor;
using Engine.Entities;
using Engine.Recources;
using Engine.Recources.Helper;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DirectionalLight = Engine.Entities.DirectionalLight;

namespace Engine.Logic
{
    /// <summary>
    /// .obsc (Obsidian Scene) file format. JSON via System.Text.Json — BCL only,
    /// no extra dependency. Models/textures/materials are referenced by their
    /// <see cref="Assets"/> field name (the same string the editor bridge already
    /// uses for the model picker). Schema version is hard-checked on load.
    /// </summary>
    public static class SceneSerialization
    {
        public const int CurrentVersion = 1;
        public const string Extension = ".obsc";

        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters =
            {
                new Vector3JsonConverter(),
                new QuaternionJsonConverter(),
                new ColorJsonConverter(),
            }
        };

        public static void SaveToFile(Scene scene, string path, Assets assets)
        {
            if (scene == null) throw new ArgumentNullException(nameof(scene));
            if (assets == null) throw new ArgumentNullException(nameof(assets));

            var doc = BuildDocument(scene, assets);
            string json = JsonSerializer.Serialize(doc, Options);
            File.WriteAllText(path, json);
            EditorBridge.Log($"SceneSerialization.SaveToFile: '{path}' ({doc.Entities.Count} entities, {doc.PointLights.Count} pls, {doc.DirectionalLights.Count} dls, {doc.Decals.Count} decals)");

            scene.FilePath = path;
            scene.Name = Path.GetFileNameWithoutExtension(path);
            scene.IsDirty = false;
        }

        public static Scene LoadFromFile(string path, Assets assets, GraphicsDevice graphicsDevice = null)
        {
            if (assets == null) throw new ArgumentNullException(nameof(assets));
            if (!File.Exists(path)) throw new FileNotFoundException("Scene file not found", path);

            string json = File.ReadAllText(path);
            SceneDocument doc;
            try { doc = JsonSerializer.Deserialize<SceneDocument>(json, Options); }
            catch (JsonException ex) { throw new InvalidDataException("Invalid .obsc JSON: " + ex.Message, ex); }
            if (doc == null) throw new InvalidDataException("Empty scene document");
            if (doc.Version != CurrentVersion)
                throw new InvalidDataException($"Unsupported .obsc version {doc.Version} (this build expects {CurrentVersion})");

            Scene scene = new Scene
            {
                Name = Path.GetFileNameWithoutExtension(path),
                FilePath = path,
                IsDirty = false,
            };

            // Models/textures by name via reflection. Same lookup as EditorBridge.
            var modelLookup = BuildAssetLookup<ModelDefinition>(assets);
            var textureLookup = BuildAssetLookup<Texture2D>(assets);
            var materialLookup = BuildAssetLookup<MaterialEffect>(assets);

            int maxId = 0;

            foreach (var rec in doc.Entities)
            {
                if (string.IsNullOrEmpty(rec.ModelKey) || !modelLookup.TryGetValue(rec.ModelKey, out var model))
                {
                    EditorBridge.Log($"LoadScene: skipping entity '{rec.Name}' — unknown ModelKey '{rec.ModelKey}'");
                    continue;
                }
                MaterialEffect material = ResolveMaterial(rec.Material, materialLookup, assets);
                Matrix rot = Matrix.CreateFromQuaternion(rec.Rotation);
                var be = new BasicEntity(model, material, rec.Position, rot, rec.Scale);
                be.Name = rec.Name ?? be.Name;
                be.IsEnabled = rec.IsEnabled;
                if (rec.Id > 0) { be.Id = rec.Id; if (rec.Id > maxId) maxId = rec.Id; }
                scene.BasicEntities.Add(be);
            }

            foreach (var rec in doc.PointLights)
            {
                var pl = new PointLight(rec.Position, rec.Radius, rec.Color, rec.Intensity,
                    castShadows: rec.CastShadows, isVolumetric: rec.IsVolumetric,
                    shadowResolution: rec.ShadowResolution, softShadowBlurAmount: rec.SoftShadowBlurAmount,
                    staticShadow: rec.StaticShadow, volumeDensity: rec.VolumetricDensity);
                pl.Name = rec.Name ?? pl.Name;
                pl.IsEnabled = rec.IsEnabled;
                if (rec.Id > 0) { pl.Id = rec.Id; if (rec.Id > maxId) maxId = rec.Id; }
                scene.PointLights.Add(pl);
            }

            foreach (var rec in doc.DirectionalLights)
            {
                var dl = new DirectionalLight(rec.Color, rec.Intensity, rec.Direction, rec.Position,
                    castShadows: rec.CastShadows, shadowSize: rec.ShadowSize, shadowDepth: rec.ShadowDepth,
                    shadowResolution: rec.ShadowResolution,
                    shadowFiltering: (DirectionalLight.ShadowFilteringTypes)rec.ShadowFiltering,
                    screenspaceshadowblur: rec.ScreenSpaceShadowBlur);
                dl.Name = rec.Name ?? dl.Name;
                dl.IsEnabled = rec.IsEnabled;
                if (rec.Id > 0) { dl.Id = rec.Id; if (rec.Id > maxId) maxId = rec.Id; }
                scene.DirectionalLights.Add(dl);
            }

            foreach (var rec in doc.Decals)
            {
                Texture2D tex = null;
                if (!string.IsNullOrEmpty(rec.TextureKey)) textureLookup.TryGetValue(rec.TextureKey, out tex);
                if (tex == null)
                {
                    EditorBridge.Log($"LoadScene: skipping decal '{rec.Name}' — unknown TextureKey '{rec.TextureKey}'");
                    continue;
                }
                Matrix rot = Matrix.CreateFromQuaternion(rec.Rotation);
                var dc = new Decal(tex, rec.Position, rot, rec.Scale);
                dc.Name = rec.Name ?? dc.Name;
                dc.IsEnabled = rec.IsEnabled;
                if (rec.Id > 0) { dc.Id = rec.Id; if (rec.Id > maxId) maxId = rec.Id; }
                scene.Decals.Add(dc);
            }

            if (doc.EnvironmentSample != null)
            {
                scene.EnvironmentSample = new EnvironmentSample(doc.EnvironmentSample.Position);
                scene.EnvironmentSample.SpecularStrength = doc.EnvironmentSample.SpecularStrength;
                scene.EnvironmentSample.DiffuseStrength = doc.EnvironmentSample.DiffuseStrength;
                scene.EnvironmentSample.AutoUpdate = doc.EnvironmentSample.AutoUpdate;
                scene.EnvironmentSample.UseSDFAO = doc.EnvironmentSample.UseSDFAO;
            }

            if (doc.MainCamera != null)
            {
                scene.MainCamera = new Camera(doc.MainCamera.Position, doc.MainCamera.Position + doc.MainCamera.Forward);
                scene.MainCamera.FieldOfView = doc.MainCamera.FieldOfView;
            }

            // Advance the global ID generator so newly-added objects can't collide.
            IdGenerator.Reseed(maxId);

            EditorBridge.Log($"SceneSerialization.LoadFromFile: '{path}' loaded ({scene.BasicEntities.Count}/{scene.PointLights.Count}/{scene.DirectionalLights.Count}/{scene.Decals.Count})");
            return scene;
        }

        private static SceneDocument BuildDocument(Scene scene, Assets assets)
        {
            var doc = new SceneDocument { Version = CurrentVersion, Name = scene.Name };

            var modelReverse = BuildReverseLookup<ModelDefinition>(assets);
            var textureReverse = BuildReverseLookup<Texture2D>(assets);

            foreach (var be in scene.BasicEntities)
            {
                if (!modelReverse.TryGetValue(be.ModelDefinition, out string modelKey))
                {
                    EditorBridge.Log($"SaveScene: entity '{be.Name}' has unregistered ModelDefinition, skipping");
                    continue;
                }
                doc.Entities.Add(new BasicEntityRecord
                {
                    Id = be.Id,
                    Name = be.Name,
                    IsEnabled = be.IsEnabled,
                    ModelKey = modelKey,
                    Position = be.Position,
                    Rotation = Quaternion.CreateFromRotationMatrix(be.RotationMatrix),
                    Scale = be.Scale,
                    Material = be.Material == null ? null : new MaterialRecord
                    {
                        DiffuseColor = be.Material.DiffuseColor,
                        Roughness = be.Material.Roughness,
                        Metallic = be.Material.Metallic,
                        EmissiveStrength = be.Material.EmissiveStrength,
                        IsTransparent = be.Material.IsTransparent,
                        MaterialType = (int)be.Material.Type,
                    },
                });
            }

            foreach (var pl in scene.PointLights)
            {
                doc.PointLights.Add(new PointLightRecord
                {
                    Id = pl.Id, Name = pl.Name, IsEnabled = pl.IsEnabled,
                    Position = pl.Position, Radius = pl.Radius, Color = pl.Color, Intensity = pl.Intensity,
                    CastShadows = pl.CastShadows, IsVolumetric = pl.IsVolumetric,
                    ShadowResolution = pl.ShadowResolution, SoftShadowBlurAmount = pl.SoftShadowBlurAmount,
                    StaticShadow = pl.StaticShadows, VolumetricDensity = pl.LightVolumeDensity,
                });
            }

            foreach (var dl in scene.DirectionalLights)
            {
                doc.DirectionalLights.Add(new DirectionalLightRecord
                {
                    Id = dl.Id, Name = dl.Name, IsEnabled = dl.IsEnabled,
                    Direction = dl.Direction, Position = dl.Position,
                    Color = dl.Color, Intensity = dl.Intensity,
                    CastShadows = dl.CastShadows, ShadowSize = dl.ShadowSize,
                    ShadowDepth = dl.ShadowDepth, ShadowResolution = dl.ShadowResolution,
                    ShadowFiltering = (int)dl.ShadowFiltering, ScreenSpaceShadowBlur = dl.ScreenSpaceShadowBlur,
                });
            }

            foreach (var dc in scene.Decals)
            {
                if (!textureReverse.TryGetValue(dc.Texture, out string texKey))
                {
                    EditorBridge.Log($"SaveScene: decal '{dc.Name}' has unregistered texture, skipping");
                    continue;
                }
                doc.Decals.Add(new DecalRecord
                {
                    Id = dc.Id, Name = dc.Name, IsEnabled = dc.IsEnabled,
                    TextureKey = texKey,
                    Position = dc.Position,
                    Rotation = Quaternion.CreateFromRotationMatrix(dc.RotationMatrix),
                    Scale = dc.Scale,
                });
            }

            if (scene.EnvironmentSample != null)
            {
                doc.EnvironmentSample = new EnvironmentSampleRecord
                {
                    Position = scene.EnvironmentSample.Position,
                    SpecularStrength = scene.EnvironmentSample.SpecularStrength,
                    DiffuseStrength = scene.EnvironmentSample.DiffuseStrength,
                    AutoUpdate = scene.EnvironmentSample.AutoUpdate,
                    UseSDFAO = scene.EnvironmentSample.UseSDFAO,
                };
            }

            if (scene.MainCamera != null)
            {
                doc.MainCamera = new CameraRecord
                {
                    Position = scene.MainCamera.Position,
                    Forward = scene.MainCamera.Forward,
                    FieldOfView = scene.MainCamera.FieldOfView,
                };
            }

            return doc;
        }

        private static MaterialEffect ResolveMaterial(MaterialRecord rec, Dictionary<string, MaterialEffect> materialLookup, Assets assets)
        {
            // No material data → fall back to the engine's default base material.
            if (rec == null) return assets.BaseMaterial?.Clone();

            MaterialEffect material = assets.BaseMaterial?.Clone();
            if (material == null) return null;
            material.DiffuseColor = rec.DiffuseColor;
            material.Roughness = rec.Roughness;
            material.Metallic = rec.Metallic;
            material.EmissiveStrength = rec.EmissiveStrength;
            material.IsTransparent = rec.IsTransparent;
            material.Type = (MaterialEffect.MaterialTypes)rec.MaterialType;
            return material;
        }

        private static Dictionary<string, T> BuildAssetLookup<T>(Assets assets) where T : class
        {
            var dict = new Dictionary<string, T>();
            FieldInfo[] fields = typeof(Assets).GetFields(BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < fields.Length; i++)
            {
                if (fields[i].FieldType != typeof(T)) continue;
                if (fields[i].GetValue(assets) is T v && v != null) dict[fields[i].Name] = v;
            }
            return dict;
        }

        private static Dictionary<T, string> BuildReverseLookup<T>(Assets assets) where T : class
        {
            var dict = new Dictionary<T, string>(ReferenceEqualityComparer.Instance);
            FieldInfo[] fields = typeof(Assets).GetFields(BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < fields.Length; i++)
            {
                if (fields[i].FieldType != typeof(T)) continue;
                if (fields[i].GetValue(assets) is T v && v != null && !dict.ContainsKey(v))
                    dict[v] = fields[i].Name;
            }
            return dict;
        }

        // --------------------------------------------------------------------
        // DTOs
        // --------------------------------------------------------------------

        public class SceneDocument
        {
            public int Version { get; set; } = CurrentVersion;
            public string Name { get; set; }
            public List<BasicEntityRecord> Entities { get; set; } = new();
            public List<PointLightRecord> PointLights { get; set; } = new();
            public List<DirectionalLightRecord> DirectionalLights { get; set; } = new();
            public List<DecalRecord> Decals { get; set; } = new();
            public EnvironmentSampleRecord EnvironmentSample { get; set; }
            public CameraRecord MainCamera { get; set; }
        }

        public class BasicEntityRecord
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public bool IsEnabled { get; set; } = true;
            public string ModelKey { get; set; }
            public Vector3 Position { get; set; }
            public Quaternion Rotation { get; set; } = Quaternion.Identity;
            public Vector3 Scale { get; set; } = Vector3.One;
            public MaterialRecord Material { get; set; }
        }

        public class MaterialRecord
        {
            public Vector3 DiffuseColor { get; set; }
            public float Roughness { get; set; } = 0.5f;
            public float Metallic { get; set; }
            public float EmissiveStrength { get; set; }
            public bool IsTransparent { get; set; }
            public int MaterialType { get; set; }
        }

        public class PointLightRecord
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public bool IsEnabled { get; set; } = true;
            public Vector3 Position { get; set; }
            public float Radius { get; set; }
            public Color Color { get; set; } = Color.White;
            public float Intensity { get; set; }
            public bool CastShadows { get; set; }
            public bool IsVolumetric { get; set; }
            public int ShadowResolution { get; set; } = 256;
            public int SoftShadowBlurAmount { get; set; }
            public bool StaticShadow { get; set; }
            public float VolumetricDensity { get; set; } = 1f;
        }

        public class DirectionalLightRecord
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public bool IsEnabled { get; set; } = true;
            public Vector3 Direction { get; set; }
            public Vector3 Position { get; set; }
            public Color Color { get; set; } = Color.White;
            public float Intensity { get; set; }
            public bool CastShadows { get; set; }
            public float ShadowSize { get; set; } = 100f;
            public float ShadowDepth { get; set; } = 100f;
            public int ShadowResolution { get; set; } = 512;
            public int ShadowFiltering { get; set; }
            public bool ScreenSpaceShadowBlur { get; set; }
        }

        public class DecalRecord
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public bool IsEnabled { get; set; } = true;
            public string TextureKey { get; set; }
            public Vector3 Position { get; set; }
            public Quaternion Rotation { get; set; } = Quaternion.Identity;
            public Vector3 Scale { get; set; } = Vector3.One;
        }

        public class EnvironmentSampleRecord
        {
            public Vector3 Position { get; set; }
            public float SpecularStrength { get; set; } = 1f;
            public float DiffuseStrength { get; set; } = 0.2f;
            public bool AutoUpdate { get; set; } = true;
            public bool UseSDFAO { get; set; }
        }

        public class CameraRecord
        {
            public Vector3 Position { get; set; }
            public Vector3 Forward { get; set; } = Vector3.UnitX;
            public float FieldOfView { get; set; } = (float)(System.Math.PI / 4);
        }

        // --------------------------------------------------------------------
        // Converters — keep files compact: [x,y,z] instead of {"X":..., "Y":...}.
        // --------------------------------------------------------------------

        private sealed class Vector3JsonConverter : JsonConverter<Vector3>
        {
            public override Vector3 Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
            {
                if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("Expected [x,y,z]");
                reader.Read(); float x = reader.GetSingle();
                reader.Read(); float y = reader.GetSingle();
                reader.Read(); float z = reader.GetSingle();
                reader.Read(); // EndArray
                return new Vector3(x, y, z);
            }
            public override void Write(Utf8JsonWriter writer, Vector3 v, JsonSerializerOptions options)
            {
                writer.WriteStartArray();
                writer.WriteNumberValue(v.X); writer.WriteNumberValue(v.Y); writer.WriteNumberValue(v.Z);
                writer.WriteEndArray();
            }
        }

        private sealed class QuaternionJsonConverter : JsonConverter<Quaternion>
        {
            public override Quaternion Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
            {
                if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("Expected [x,y,z,w]");
                reader.Read(); float x = reader.GetSingle();
                reader.Read(); float y = reader.GetSingle();
                reader.Read(); float z = reader.GetSingle();
                reader.Read(); float w = reader.GetSingle();
                reader.Read();
                return new Quaternion(x, y, z, w);
            }
            public override void Write(Utf8JsonWriter writer, Quaternion q, JsonSerializerOptions options)
            {
                writer.WriteStartArray();
                writer.WriteNumberValue(q.X); writer.WriteNumberValue(q.Y);
                writer.WriteNumberValue(q.Z); writer.WriteNumberValue(q.W);
                writer.WriteEndArray();
            }
        }

        private sealed class ColorJsonConverter : JsonConverter<Color>
        {
            public override Color Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
            {
                if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("Expected [r,g,b,a]");
                reader.Read(); byte r = (byte)reader.GetInt32();
                reader.Read(); byte g = (byte)reader.GetInt32();
                reader.Read(); byte b = (byte)reader.GetInt32();
                reader.Read(); byte a = (byte)reader.GetInt32();
                reader.Read();
                return new Color(r, g, b, a);
            }
            public override void Write(Utf8JsonWriter writer, Color c, JsonSerializerOptions options)
            {
                writer.WriteStartArray();
                writer.WriteNumberValue(c.R); writer.WriteNumberValue(c.G);
                writer.WriteNumberValue(c.B); writer.WriteNumberValue(c.A);
                writer.WriteEndArray();
            }
        }
    }
}
