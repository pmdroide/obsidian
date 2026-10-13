using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using Engine.Editor;
using Engine.Components;
using Engine.Entities;
using Engine.Physics;
using Engine.Recources;
using Engine.Recources.Helper;
using Engine.Renderer.Lighting;
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
        // 2: BasicEntity.IsEnabled is meaningful (version 1 saved a stuck false default).
        public const int CurrentVersion = 2;
        public const string Extension = ".obsc";

        // Shared with prefab files (Prefab), which hold one gameobject record in the same format.
        internal static JsonSerializerOptions JsonOptions => Options;

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
            doc.Lighting = new LightingRecord
            {
                Settings = scene.Lighting,
                ProbeDataFile = SaveBakedProbes(scene, path),
            };
            string json = JsonSerializer.Serialize(doc, Options);
            File.WriteAllText(path, json);
            EditorBridge.Log($"SceneSerialization.SaveToFile: '{path}' ({doc.Entities.Count} entities, {doc.PointLights.Count} pls, {doc.SpotLights.Count} spots, {doc.DirectionalLights.Count} dls, {doc.Decals.Count} decals)");

            scene.FilePath = path;
            scene.Name = Path.GetFileNameWithoutExtension(path);
            scene.IsDirty = false;
        }

        public static Scene LoadFromFile(string path, Assets assets, GraphicsDevice graphicsDevice = null)
        {
            if (assets == null) throw new ArgumentNullException(nameof(assets));
            var builder = new SceneBuilder(ReadFile(path), assets);
            while (builder.Step()) { }
            return builder.Scene;
        }

        /// <summary>
        /// Reads and parses a .obsc file and its baked probes without creating any engine object, so it can run
        /// on a worker thread (<see cref="SceneLoadOperation"/>). <paramref name="progress"/> gets (bytes read,
        /// total bytes) as the files stream in. <see cref="SceneBuilder"/> turns the result into a <see cref="Scene"/>.
        /// </summary>
        public static SceneFile ReadFile(string path, Action<long, long> progress = null, CancellationToken cancel = default)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("Scene file not found", path);

            long sceneBytes = new FileInfo(path).Length;
            // Bakes are saved as "<scene>.probes" (SaveBakedProbes), so the total is known before the JSON names it.
            string expectedProbes = Path.ChangeExtension(path, ".probes");
            long expectedTotal = sceneBytes + (File.Exists(expectedProbes) ? new FileInfo(expectedProbes).Length : 0);
            byte[] json = ReadAllBytes(path, cancel, read => progress?.Invoke(read, expectedTotal));
            SceneDocument doc;
            try { doc = JsonSerializer.Deserialize<SceneDocument>(json, Options); }
            catch (JsonException ex) { throw new InvalidDataException("Invalid .obsc JSON: " + ex.Message, ex); }
            if (doc == null) throw new InvalidDataException("Empty scene document");
            if (doc.Version < 1 || doc.Version > CurrentVersion)
                throw new InvalidDataException($"Unsupported .obsc version {doc.Version} (this build expects {CurrentVersion})");

            var file = new SceneFile { Path = path, Document = doc, Bytes = sceneBytes };
            if (doc.Lighting != null)
            {
                // The probe file is usually the bulk of a baked scene, so it counts towards the progress too.
                string probePath = ProbePath(doc.Lighting.ProbeDataFile, path);
                long probeBytes = probePath != null && File.Exists(probePath) ? new FileInfo(probePath).Length : 0;
                cancel.ThrowIfCancellationRequested();
                file.BakedProbes = LoadBakedProbes(doc.Lighting.ProbeDataFile, path);
                file.Bytes += probeBytes;
            }
            progress?.Invoke(file.Bytes, file.Bytes);
            return file;
        }

        private static byte[] ReadAllBytes(string path, CancellationToken cancel, Action<long> progress)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
            long length = stream.Length;
            var buffer = new byte[length];
            int read = 0;
            while (read < length)
            {
                cancel.ThrowIfCancellationRequested();
                int n = stream.Read(buffer, read, (int)Math.Min(1 << 16, length - read));
                if (n <= 0) throw new EndOfStreamException($"'{path}' ended early");
                read += n;
                progress?.Invoke(read);
            }
            return buffer;
        }

        /// <summary>A parsed scene file (see <see cref="ReadFile"/>): plain data, nothing in the engine refers to it yet.</summary>
        public sealed class SceneFile
        {
            public string Path;
            public SceneDocument Document;
            public ProbeVolumeData BakedProbes;
            /// <summary>Size of the .obsc plus its .probes file.</summary>
            public long Bytes;
        }

        /// <summary>
        /// Builds a <see cref="Scene"/> from a <see cref="SceneFile"/> one object per <see cref="Step"/>, so a load can be
        /// spread over several frames (<see cref="SceneLoadOperation"/>). Runs on the game thread (gameobject IDs,
        /// components). The scene isn't live until it is passed to <see cref="SceneManager.SetActiveScene"/>.
        /// </summary>
        public sealed class SceneBuilder
        {
            private readonly SceneFile _file;
            private readonly Assets _assets;
            private readonly List<Action> _steps = new();
            private readonly Dictionary<string, Texture2D> _textures;
            private readonly Dictionary<string, MaterialEffect> _materials;
            // Persisted IDs are what selection, picking and the editor bridge address objects by.
            // A missing or duplicate ID gets a fresh one once the generator is past every saved ID.
            private int _maxId;
            private readonly HashSet<int> _usedIds = new();
            private readonly List<TransformableObject> _needsNewId = new();
            private int _next;

            public Scene Scene { get; }
            /// <summary>One step per gameobject, light and decal, plus a last one (camera, environment, IDs).</summary>
            public int TotalSteps => _steps.Count;
            public int StepsDone => _next;
            public bool IsDone => _next >= _steps.Count;

            public SceneBuilder(SceneFile file, Assets assets)
            {
                _file = file ?? throw new ArgumentNullException(nameof(file));
                _assets = assets ?? throw new ArgumentNullException(nameof(assets));
                Scene = new Scene
                {
                    Name = System.IO.Path.GetFileNameWithoutExtension(file.Path),
                    FilePath = file.Path,
                    IsDirty = false,
                };

                // Textures/materials by name via reflection; models through Assets.FindModel (built-ins and imports).
                _textures = BuildAssetLookup<Texture2D>(assets);
                _materials = BuildAssetLookup<MaterialEffect>(assets);

                SceneDocument doc = file.Document;
                foreach (var rec in doc.Entities) _steps.Add(() => AddEntity(rec, doc.Version));
                foreach (var rec in doc.PointLights) _steps.Add(() => AddPointLight(rec));
                foreach (var rec in doc.SpotLights ?? new List<SpotLightRecord>()) _steps.Add(() => AddSpotLight(rec));
                foreach (var rec in doc.DirectionalLights) _steps.Add(() => AddDirectionalLight(rec));
                foreach (var rec in doc.Decals) _steps.Add(() => AddDecal(rec));
                _steps.Add(Finish);
            }

            /// <summary>Builds the next object. False once the scene is complete.</summary>
            public bool Step()
            {
                if (IsDone) return false;
                _steps[_next++]();
                return !IsDone;
            }

            private void ClaimId(TransformableObject obj, int persistedId)
            {
                if (persistedId > 0 && _usedIds.Add(persistedId))
                {
                    obj.Id = persistedId;
                    _maxId = Math.Max(_maxId, persistedId);
                }
                else _needsNewId.Add(obj);
            }

            private void AddEntity(BasicEntityRecord rec, int version)
            {
                // A prefab instance takes the prefab's current values, except the ones it overrides.
                if (!string.IsNullOrEmpty(rec.Prefab))
                {
                    Prefab prefab = Prefab.Load(rec.Prefab);
                    if (prefab != null) rec = PrefabUtility.Sync(prefab, rec, rec.PrefabOverrides);
                    else EditorBridge.Log($"LoadScene: prefab '{rec.Prefab}' of '{rec.Name}' not found, using the values saved in the scene");
                }
                BasicEntity be = RestoreEntity(rec, version, _assets, _textures, _materials);
                if (be == null) return;
                ClaimId(be, rec.Id);
                Scene.BasicEntities.Add(be);
            }

            private void AddPointLight(PointLightRecord rec)
            {
                var pl = new PointLight(rec.Position, rec.Radius, rec.Color, rec.Intensity,
                    castShadows: rec.CastShadows, isVolumetric: rec.IsVolumetric,
                    shadowResolution: rec.ShadowResolution, softShadowBlurAmount: rec.SoftShadowBlurAmount,
                    staticShadow: rec.StaticShadow, volumeDensity: rec.VolumetricDensity);
                pl.Name = rec.Name ?? pl.Name;
                pl.IsEnabled = rec.IsEnabled;
                ClaimId(pl, rec.Id);
                Scene.PointLights.Add(pl);
            }

            private void AddSpotLight(SpotLightRecord rec)
            {
                var sl = new SpotLight(rec.Position, rec.Radius, rec.Color, rec.Intensity,
                    Matrix.CreateFromQuaternion(rec.Rotation), rec.SpotAngle, rec.InnerSpotAngle,
                    castShadows: rec.CastShadows, isVolumetric: rec.IsVolumetric,
                    shadowResolution: rec.ShadowResolution, softShadowBlurAmount: rec.SoftShadowBlurAmount,
                    staticShadow: rec.StaticShadow, volumeDensity: rec.VolumetricDensity);
                sl.Name = rec.Name ?? sl.Name;
                sl.IsEnabled = rec.IsEnabled;
                ClaimId(sl, rec.Id);
                Scene.PointLights.Add(sl);
            }

            private void AddDirectionalLight(DirectionalLightRecord rec)
            {
                var dl = new DirectionalLight(rec.Color, rec.Intensity, rec.Direction, rec.Position,
                    castShadows: rec.CastShadows, shadowSize: rec.ShadowSize, shadowDepth: rec.ShadowDepth,
                    shadowResolution: rec.ShadowResolution,
                    shadowFiltering: (DirectionalLight.ShadowFilteringTypes)rec.ShadowFiltering,
                    screenspaceshadowblur: rec.ScreenSpaceShadowBlur);
                dl.Name = rec.Name ?? dl.Name;
                dl.IsEnabled = rec.IsEnabled;
                ClaimId(dl, rec.Id);
                Scene.DirectionalLights.Add(dl);
            }

            private void AddDecal(DecalRecord rec)
            {
                // A Content texture picked in the Inspector; a built-in Assets field otherwise. A path that
                // fails to load keeps the decal (on the default texture) and its path, so saving doesn't lose it.
                Texture2D tex = string.IsNullOrEmpty(rec.TexturePath) ? null : Decal.LoadTexture(rec.TexturePath);
                if (tex == null && !string.IsNullOrEmpty(rec.TextureKey)) _textures.TryGetValue(rec.TextureKey, out tex);
                if (tex == null && !string.IsNullOrEmpty(rec.TexturePath)) _textures.TryGetValue(nameof(Assets.DefaultDecal), out tex);
                if (tex == null)
                {
                    EditorBridge.Log($"LoadScene: skipping decal '{rec.Name}' — unknown TextureKey '{rec.TextureKey}'");
                    return;
                }
                Matrix rot = Matrix.CreateFromQuaternion(rec.Rotation);
                var dc = new Decal(tex, rec.Position, rot, rec.Scale);
                dc.TexturePath = string.IsNullOrEmpty(rec.TexturePath) ? null : rec.TexturePath;
                dc.Name = rec.Name ?? dc.Name;
                dc.IsEnabled = rec.IsEnabled;
                ClaimId(dc, rec.Id);
                Scene.Decals.Add(dc);
            }

            private void Finish()
            {
                SceneDocument doc = _file.Document;
                Scene scene = Scene;
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
                    foreach (var record in doc.MainCamera.Components ?? new())
                    {
                        var component = ComponentRegistry.Restore(record);
                        if (component == null || !scene.MainCamera.AddComponent(component))
                            EditorBridge.Log($"LoadScene: main camera can't use component '{record.Type}'");
                    }
                }

                if (doc.Lighting != null)
                {
                    scene.Lighting = doc.Lighting.Settings ?? new LightingSettings();
                    scene.BakedProbes = _file.BakedProbes;
                }

                // Advance the global ID generator so newly-added objects can't collide.
                IdGenerator.Reseed(_maxId);
                // The probe is not saved with an ID, so its constructor ID may equal a loaded one.
                if (scene.EnvironmentSample != null) _needsNewId.Add(scene.EnvironmentSample);
                foreach (var obj in _needsNewId) obj.Id = IdGenerator.GetNewId();
                scene.Environment = doc.Environment ?? new EnvironmentSettings();
                scene.Environment.Normalize();

                EditorBridge.Log($"SceneSerialization.LoadFromFile: '{_file.Path}' loaded ({scene.BasicEntities.Count}/{scene.PointLights.Count}/{scene.DirectionalLights.Count}/{scene.Decals.Count})");
            }
        }

        /// <summary>
        /// Builds a gameobject from its record (scenes and prefabs). It gets a fresh ID and isn't registered for
        /// drawing yet. Null when the model key is unknown.
        /// </summary>
        internal static BasicEntity RestoreEntity(BasicEntityRecord rec, int version, Assets assets,
            Dictionary<string, Texture2D> textures = null, Dictionary<string, MaterialEffect> materials = null)
        {
            ModelDefinition model = string.IsNullOrEmpty(rec.ModelKey) ? null : assets.FindModel(rec.ModelKey);
            if (model == null)
            {
                EditorBridge.Log($"LoadScene: skipping entity '{rec.Name}' — unknown ModelKey '{rec.ModelKey}'");
                return null;
            }
            textures ??= BuildAssetLookup<Texture2D>(assets);
            materials ??= BuildAssetLookup<MaterialEffect>(assets);
            MaterialEffect material = ResolveMaterial(rec.Material, materials, textures, assets);
            Matrix rot = Matrix.CreateFromQuaternion(rec.Rotation);
            var be = new BasicEntity(model, material, rec.Position, rot, rec.Scale);
            be.Name = rec.Name ?? be.Name;
            // Version 1 entities were never enabled (and the flag hid nothing), so enable them.
            be.IsEnabled = rec.IsEnabled || version < 2;
            be.Role = Enum.IsDefined(rec.Role) ? rec.Role : GameObjectRole.Default;
            be.Persistent = rec.Persistent;
            be.PrefabPath = string.IsNullOrEmpty(rec.Prefab) ? null : rec.Prefab;
            be.BonePose.Load(rec.BonePose);
            foreach (var record in rec.Components ?? new())
            {
                var component = ComponentRegistry.Restore(record);
                if (component == null)
                    EditorBridge.Log($"LoadScene: unknown component '{record.Type}' on '{be.Name}'");
                else if (!be.AddComponent(component))
                    EditorBridge.Log($"LoadScene: duplicate component '{record.Type}' on '{be.Name}' ignored");
            }
            // Scenes saved before Physics became a component stored it as its own record.
            if (rec.Physics != null && rec.Physics.Type != PhysicsBodyType.None && be.Physics == null)
                be.AddComponent(new PhysicsComponent { BodyType = rec.Physics.Type, Mass = rec.Physics.Mass });
            return be;
        }

        /// <summary>The record a gameobject saves as (without its prefab link); null when its model isn't registered.</summary>
        internal static BasicEntityRecord CaptureEntity(BasicEntity be, Assets assets) =>
            CaptureEntity(be, BuildReverseLookup<ModelDefinition>(assets), BuildReverseLookup<Texture2D>(assets));

        private static BasicEntityRecord CaptureEntity(BasicEntity be, Dictionary<ModelDefinition, string> modelReverse,
            Dictionary<Texture2D, string> textureReverse)
        {
            if (!modelReverse.TryGetValue(be.ModelDefinition, out string modelKey)) return null;
            return new BasicEntityRecord
            {
                Id = be.Id,
                Name = be.Name,
                IsEnabled = be.IsEnabled,
                Role = be.Role,
                Persistent = be.Persistent,
                ModelKey = modelKey,
                BonePose = be.BonePose.ToRecord(),
                Components = be.Components.Select(ComponentRegistry.Capture).ToList(),
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
                    // Texture refs persisted by their Assets-field name. Reverse
                    // lookup returns null for textures embedded in the model file
                    // (those aren't tracked in the Assets registry) — that's the
                    // signal to fall back to BaseMaterial on load.
                    AlbedoKey = LookupKey(textureReverse, be.Material.AlbedoMap),
                    NormalKey = LookupKey(textureReverse, be.Material.NormalMap),
                    RoughnessKey = LookupKey(textureReverse, be.Material.RoughnessMap),
                    MetallicKey = LookupKey(textureReverse, be.Material.MetallicMap),
                    MaskKey = LookupKey(textureReverse, be.Material.Mask),
                },
            };
        }

        // Baked probes live in "<scene>.probes" next to the .obsc (binary, can be large).
        // Returns the file name to store in the scene, or null when nothing is baked.
        private static string SaveBakedProbes(Scene scene, string scenePath)
        {
            // A cleared bake just stops being referenced; an old .probes file is left untouched.
            if (scene.BakedProbes == null) return null;
            string probePath = Path.ChangeExtension(scenePath, ".probes");
            scene.BakedProbes.Save(probePath);
            return Path.GetFileName(probePath);
        }

        private static string ProbePath(string fileName, string scenePath) =>
            string.IsNullOrEmpty(fileName) ? null : Path.Combine(Path.GetDirectoryName(scenePath) ?? "", Path.GetFileName(fileName));

        private static ProbeVolumeData LoadBakedProbes(string fileName, string scenePath)
        {
            string probePath = ProbePath(fileName, scenePath);
            if (probePath == null) return null;
            if (!File.Exists(probePath))
            {
                EditorBridge.Log($"LoadScene: baked lighting '{probePath}' not found — rebake to restore it");
                return null;
            }
            try { return ProbeVolumeData.Load(probePath); }
            catch (Exception ex)
            {
                EditorBridge.Log($"LoadScene: couldn't read baked lighting '{probePath}': {ex.Message}");
                return null;
            }
        }

        private static SceneDocument BuildDocument(Scene scene, Assets assets)
        {
            var doc = new SceneDocument { Version = CurrentVersion, Name = scene.Name, Environment = scene.Environment.Clone() };

            var modelReverse = BuildReverseLookup<ModelDefinition>(assets);
            var textureReverse = BuildReverseLookup<Texture2D>(assets);

            foreach (var be in scene.BasicEntities)
            {
                // Script-spawned objects (e.g. network players) only exist for the Play session.
                if (be.IsRuntimeSpawned) continue;
                BasicEntityRecord rec = CaptureEntity(be, modelReverse, textureReverse);
                if (rec == null)
                {
                    EditorBridge.Log($"SaveScene: entity '{be.Name}' has unregistered ModelDefinition, skipping");
                    continue;
                }
                if (!string.IsNullOrEmpty(be.PrefabPath))
                {
                    // Only what differs from the prefab is the instance's own; the rest follows later prefab edits.
                    rec.Prefab = be.PrefabPath;
                    Prefab prefab = Prefab.Load(be.PrefabPath);
                    rec.PrefabOverrides = prefab != null
                        ? PrefabUtility.Overrides(prefab, rec).ToList()
                        : new List<string> { PrefabUtility.AllOverridden };
                }
                doc.Entities.Add(rec);
            }

            foreach (var pl in scene.PointLights)
            {
                if (pl is SpotLight sl)
                {
                    doc.SpotLights.Add(new SpotLightRecord
                    {
                        Id = sl.Id, Name = sl.Name, IsEnabled = sl.IsEnabled,
                        Position = sl.Position, Radius = sl.Radius, Color = sl.Color, Intensity = sl.Intensity,
                        CastShadows = sl.CastShadows, IsVolumetric = sl.IsVolumetric,
                        ShadowResolution = sl.ShadowResolution, SoftShadowBlurAmount = sl.SoftShadowBlurAmount,
                        StaticShadow = sl.StaticShadows, VolumetricDensity = sl.LightVolumeDensity,
                        Rotation = Quaternion.CreateFromRotationMatrix(sl.RotationMatrix),
                        SpotAngle = sl.SpotAngle, InnerSpotAngle = sl.InnerSpotAngle,
                    });
                    continue;
                }
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
                string texKey = dc.Texture == null ? null : textureReverse.GetValueOrDefault(dc.Texture);
                if (texKey == null && string.IsNullOrEmpty(dc.TexturePath))
                {
                    EditorBridge.Log($"SaveScene: decal '{dc.Name}' has unregistered texture, skipping");
                    continue;
                }
                doc.Decals.Add(new DecalRecord
                {
                    Id = dc.Id, Name = dc.Name, IsEnabled = dc.IsEnabled,
                    TextureKey = string.IsNullOrEmpty(dc.TexturePath) ? texKey : null,
                    TexturePath = string.IsNullOrEmpty(dc.TexturePath) ? null : dc.TexturePath,
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
                    Components = scene.MainCamera.Components.Select(ComponentRegistry.Capture).ToList(),
                };
            }

            return doc;
        }

        private static MaterialEffect ResolveMaterial(MaterialRecord rec, Dictionary<string, MaterialEffect> materialLookup, Dictionary<string, Texture2D> textureLookup, Assets assets)
        {
            // No material data → entity was created with the model's embedded
            // materials (e.g. Sponza). Returning null tells BasicEntity to keep
            // those embedded effects instead of overwriting them with a blank
            // BaseMaterial clone that would drop every texture in the model.
            if (rec == null) return null;

            MaterialEffect material = assets.BaseMaterial?.Clone();
            if (material == null) return null;
            material.DiffuseColor = rec.DiffuseColor;
            material.Roughness = rec.Roughness;
            material.Metallic = rec.Metallic;
            material.EmissiveStrength = rec.EmissiveStrength;
            material.IsTransparent = rec.IsTransparent;
            material.Type = (MaterialEffect.MaterialTypes)rec.MaterialType;

            // Texture restore: each setter ignores null, so missing keys keep
            // BaseMaterial's defaults. Unknown keys (e.g. asset renamed) are
            // logged once per record so the user can fix the .obsc file.
            if (rec.AlbedoKey != null && textureLookup.TryGetValue(rec.AlbedoKey, out var albedo)) material.AlbedoMap = albedo;
            else if (rec.AlbedoKey != null) EditorBridge.Log($"LoadScene: AlbedoKey '{rec.AlbedoKey}' not in Assets");
            if (rec.NormalKey != null && textureLookup.TryGetValue(rec.NormalKey, out var normal)) material.NormalMap = normal;
            else if (rec.NormalKey != null) EditorBridge.Log($"LoadScene: NormalKey '{rec.NormalKey}' not in Assets");
            if (rec.RoughnessKey != null && textureLookup.TryGetValue(rec.RoughnessKey, out var rough)) material.RoughnessMap = rough;
            else if (rec.RoughnessKey != null) EditorBridge.Log($"LoadScene: RoughnessKey '{rec.RoughnessKey}' not in Assets");
            if (rec.MetallicKey != null && textureLookup.TryGetValue(rec.MetallicKey, out var metal)) material.MetallicMap = metal;
            else if (rec.MetallicKey != null) EditorBridge.Log($"LoadScene: MetallicKey '{rec.MetallicKey}' not in Assets");
            if (rec.MaskKey != null && textureLookup.TryGetValue(rec.MaskKey, out var mask)) material.Mask = mask;
            else if (rec.MaskKey != null) EditorBridge.Log($"LoadScene: MaskKey '{rec.MaskKey}' not in Assets");

            return material;
        }

        private static string LookupKey<T>(Dictionary<T, string> reverse, T value) where T : class
        {
            if (value == null) return null;
            return reverse.TryGetValue(value, out string k) ? k : null;
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
            // Runtime-imported models are saved by their registry key (Assets.FindModel resolves it on load).
            if (typeof(T) == typeof(ModelDefinition))
                foreach (var kvp in assets.DynamicModels)
                    if (kvp.Value is T model && !dict.ContainsKey(model)) dict[model] = kvp.Key;
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
            // Optional: files saved before spot lights existed have none
            public List<SpotLightRecord> SpotLights { get; set; } = new();
            public List<DirectionalLightRecord> DirectionalLights { get; set; } = new();
            public List<DecalRecord> Decals { get; set; } = new();
            public EnvironmentSampleRecord EnvironmentSample { get; set; }
            public EnvironmentSettings Environment { get; set; }
            public CameraRecord MainCamera { get; set; }
            // Optional: older v1 files without it load with default lighting settings
            public LightingRecord Lighting { get; set; }
        }

        public class LightingRecord
        {
            public LightingSettings Settings { get; set; }
            // File name (same folder as the .obsc) of the baked probe volume, or null
            public string ProbeDataFile { get; set; }
        }

        public class BasicEntityRecord
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public bool IsEnabled { get; set; } = true;
            // Missing in older scenes, which load as Default.
            [JsonConverter(typeof(JsonStringEnumConverter))]
            public GameObjectRole Role { get; set; } = GameObjectRole.Default;
            // Only written when true, so scenes without persistent gameobjects don't change.
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
            public bool Persistent { get; set; }
            public string ModelKey { get; set; }
            public Vector3 Position { get; set; }
            public Quaternion Rotation { get; set; } = Quaternion.Identity;
            public Vector3 Scale { get; set; } = Vector3.One;
            public MaterialRecord Material { get; set; }
            // Legacy (read-only): physics is saved as a PhysicsComponent in Components now.
            public PhysicsRecord Physics { get; set; }
            public List<ComponentRecord> Components { get; set; } = new();
            /// <summary>Prefab instance: the Content-relative .prefab it comes from ("Prefabs/Crate.prefab"); null otherwise.</summary>
            public string Prefab { get; set; }
            /// <summary>
            /// Prefab instance: the values that differ from the prefab (<see cref="PrefabUtility.Overrides"/>); every other
            /// value is taken from the prefab on load, so prefab edits reach the instance.
            /// </summary>
            public List<string> PrefabOverrides { get; set; }
            /// <summary>
            /// Skinned models: posed bones (<see cref="Animation.BonePose"/>), bone name ("" = the Armature) -> rotation
            /// in the bone's own space. Only written when something is posed.
            /// </summary>
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public Dictionary<string, Quaternion> BonePose { get; set; }
        }

        public class PhysicsRecord
        {
            [JsonConverter(typeof(JsonStringEnumConverter))]
            public PhysicsBodyType Type { get; set; }
            public float Mass { get; set; } = 1f;
        }

        public class MaterialRecord
        {
            public Vector3 DiffuseColor { get; set; }
            public float Roughness { get; set; } = 0.5f;
            public float Metallic { get; set; }
            public float EmissiveStrength { get; set; }
            public bool IsTransparent { get; set; }
            public int MaterialType { get; set; }

            // Texture references by Assets-field name. null = field not customized
            // or texture is embedded in the model (handled by the null-material path).
            public string AlbedoKey { get; set; }
            public string NormalKey { get; set; }
            public string RoughnessKey { get; set; }
            public string MetallicKey { get; set; }
            public string MaskKey { get; set; }
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

        public class SpotLightRecord : PointLightRecord
        {
            public Quaternion Rotation { get; set; } = Quaternion.Identity;
            public float SpotAngle { get; set; } = 60f;
            public float InnerSpotAngle { get; set; } = 40f;
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
            /// <summary>Built-in texture: the name of an <c>Assets</c> Texture2D field. Used when TexturePath is empty.</summary>
            public string TextureKey { get; set; }
            /// <summary>Content-relative texture source path picked in the Inspector ("Textures/stain.png").</summary>
            public string TexturePath { get; set; }
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
            // Script Behaviours on the main camera. Missing in older scenes.
            public List<ComponentRecord> Components { get; set; } = new();
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
