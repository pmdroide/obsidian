using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Engine.Editor;
using Engine.Recources;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Renderer.Features
{
    /// <summary>
    /// The project's render features: user full-screen shaders the renderer draws at an
    /// <see cref="RenderFeatureEvent"/> (<see cref="RenderModules.RenderFeatureRenderModule"/>), around the built-in
    /// passes. Saved in <c>Content/System/RenderFeatures.json</c>, in run order; each shader lives in
    /// <c>Content/RenderFeatures/</c> and is compiled by <see cref="RenderFeatureCompiler"/> into a loose .xnb next to
    /// it (recompiled whenever the .fx changes on disk, while a source checkout exists).
    ///
    /// Game thread only, except <see cref="Describe"/> (a published copy) and <see cref="Changed"/> (raised on
    /// the game thread). Anvil edits through <c>IEditorBridge</c>; scripts may change a feature's values with
    /// <see cref="Find"/> during Play (not saved; Stop restores them).
    /// </summary>
    public static class RenderFeatures
    {
        /// <summary>Content-relative settings file. System/ is engine-managed and hidden from Anvil's Assets panel.</summary>
        public const string FileName = "System/RenderFeatures.json";
        /// <summary>Content-relative folder new feature shaders are written to.</summary>
        public const string ShaderFolder = "RenderFeatures";

        // Frames between checks of the shader files' write times (hot reload).
        private const int SourcePollFrames = 30;

        public sealed class Data
        {
            public List<RenderFeature> Features { get; set; } = new List<RenderFeature>();
        }

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        private static readonly List<RenderFeature> _features = new List<RenderFeature>();
        // The saved values, restored over script changes on Stop.
        private static readonly Dictionary<Guid, RenderFeature> _saved = new Dictionary<Guid, RenderFeature>();
        // Compile results waiting to be applied (effects load on the game thread).
        private static readonly ConcurrentQueue<Action> _completed = new ConcurrentQueue<Action>();
        private static readonly HashSet<Guid> _compiling = new HashSet<Guid>();
        private static readonly HashSet<Guid> _compileAgain = new HashSet<Guid>();
        private static IReadOnlyList<RenderPipelineLayer> _published = Array.Empty<RenderPipelineLayer>();
        private static IServiceProvider _services;
        private static int _frame;

        /// <summary>The source Content folder features are read from and written to (tests point it elsewhere).</summary>
        internal static string SourceRootOverride;
        private static string SourceRoot => SourceRootOverride ?? GameInfo.SourceContentRoot;

        /// <summary>Raised on the game thread after a feature, its status or the list changed.</summary>
        public static event Action Changed;

        /// <summary>Every feature in run order (game thread).</summary>
        public static IReadOnlyList<RenderFeature> All => _features;

        /// <summary>A render feature by name (case-insensitive), or null. For scripts: changes last until Stop.</summary>
        public static RenderFeature Find(string name) =>
            _features.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));

        /// <summary>The render pipeline as rows for the editor: built-in passes, injection points and features. Any thread.</summary>
        public static IReadOnlyList<RenderPipelineLayer> Describe() => _published;

        /// <summary>Absolute path of a feature's .fx in the source Content folder.</summary>
        public static string ShaderSourcePath(RenderFeature feature) =>
            string.IsNullOrEmpty(feature?.Shader) ? null
                : Path.Combine(SourceRoot, feature.Shader.Replace('/', Path.DirectorySeparatorChar));

        #region Loading

        /// <summary>
        /// Reads RenderFeatures.json and loads (or compiles) every shader. Called by
        /// <see cref="Logic.ScreenManager"/> once the content manager exists, and again after a project reset.
        /// </summary>
        public static void Load(ContentManager content)
        {
            _services = content?.ServiceProvider ?? _services;
            foreach (RenderFeature feature in _features) UnloadEffect(feature);
            _features.Clear();
            _saved.Clear();
            while (_completed.TryDequeue(out _)) { }

            foreach (RenderFeature feature in Read().Features)
            {
                _features.Add(feature);
                _saved[feature.Id] = feature.CloneSettings();
                Prepare(feature);
            }
            Publish();
        }

        /// <summary>Reads the settings file without touching the loaded features.</summary>
        public static Data Read()
        {
            string path = SourceRootOverride != null
                ? Path.Combine(SourceRootOverride, FileName.Replace('/', Path.DirectorySeparatorChar))
                : GameInfo.ResolveContentFile(FileName);
            try
            {
                if (File.Exists(path))
                {
                    Data data = JsonSerializer.Deserialize<Data>(File.ReadAllText(path), JsonOptions) ?? new Data();
                    data.Features = (data.Features ?? new List<RenderFeature>()).Where(f => f != null).ToList();
                    foreach (RenderFeature feature in data.Features) feature.Normalize();
                    // Duplicate ids (a hand-copied entry) would make the editor edit the wrong row.
                    var ids = new HashSet<Guid>();
                    foreach (RenderFeature feature in data.Features)
                        if (!ids.Add(feature.Id)) { feature.Id = Guid.NewGuid(); ids.Add(feature.Id); }
                    return data;
                }
            }
            catch (Exception e)
            {
                EditorBridge.Log($"RenderFeatures: failed to read '{path}': {e.Message}");
            }
            return new Data();
        }

        /// <summary>Writes the features to the source Content folder (the build copies it next to the game).</summary>
        public static void Save()
        {
            var data = new Data { Features = _features.Select(f => f.CloneSettings()).ToList() };
            string path = Path.Combine(SourceRoot, FileName.Replace('/', Path.DirectorySeparatorChar));
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonSerializer.Serialize(data, JsonOptions));
            }
            catch (Exception e)
            {
                EditorBridge.Log($"RenderFeatures: failed to save '{path}': {e.Message}");
            }
            _saved.Clear();
            foreach (RenderFeature feature in _features) _saved[feature.Id] = feature.CloneSettings();
        }

        // Loads the compiled shader, or compiles it first when the .fx is newer (or was never compiled).
        private static void Prepare(RenderFeature feature)
        {
            if (string.IsNullOrEmpty(feature.Shader))
            {
                SetStatus(feature, RenderFeatureStatus.Error, "No shader file set.");
                return;
            }

            string source = ShaderSourcePath(feature);
            bool hasSource = File.Exists(source);
            string sourceXnb = RenderFeatureCompiler.CompiledPath(SourceRoot, feature.Shader);
            DateTime sourceTime = hasSource ? File.GetLastWriteTimeUtc(source) : default;

            if (hasSource && (!File.Exists(sourceXnb) || File.GetLastWriteTimeUtc(sourceXnb) < sourceTime))
            {
                // The previous build runs until the new one is ready.
                if (File.Exists(sourceXnb)) LoadEffect(feature, sourceXnb);
                StartCompile(feature);
                return;
            }

            // A shipped game has only the copy next to the executable.
            string compiled = File.Exists(sourceXnb)
                ? sourceXnb
                : RenderFeatureCompiler.CompiledPath(Path.Combine(AppContext.BaseDirectory, "Content"), feature.Shader);
            feature.CompiledSourceTimeUtc = sourceTime;
            if (File.Exists(compiled)) LoadEffect(feature, compiled);
            else SetStatus(feature, RenderFeatureStatus.NotCompiled, "Shader file not found: " + feature.Shader);
        }

        private static void StartCompile(RenderFeature feature)
        {
            if (!_compiling.Add(feature.Id))
            {
                _compileAgain.Add(feature.Id);
                return;
            }

            Guid id = feature.Id;
            string shader = feature.Shader;
            string root = SourceRoot;
            string source = ShaderSourcePath(feature);
            DateTime sourceTime = File.Exists(source) ? File.GetLastWriteTimeUtc(source) : default;
            feature.CompiledSourceTimeUtc = sourceTime;
            SetStatus(feature, RenderFeatureStatus.Compiling, null);

            Task.Run(() =>
            {
                RenderFeatureCompiler.Result result = RenderFeatureCompiler.Compile(root, shader);
                _completed.Enqueue(() => FinishCompile(id, shader, result));
            });
        }

        private static void FinishCompile(Guid id, string shader, RenderFeatureCompiler.Result result)
        {
            _compiling.Remove(id);
            RenderFeature feature = _features.FirstOrDefault(f => f.Id == id);
            if (feature == null) return;

            bool again = _compileAgain.Remove(id) || feature.Shader != shader;
            if (feature.Shader == shader)
            {
                if (result.Success)
                {
                    LoadEffect(feature, RenderFeatureCompiler.CompiledPath(SourceRoot, shader));
                    EditorBridge.Log($"RenderFeatures: compiled '{feature.Name}' ({shader})");
                }
                else
                {
                    // The previous good shader (if any) keeps running.
                    SetStatus(feature, RenderFeatureStatus.Error, result.Error);
                    EditorBridge.Log($"RenderFeatures: '{feature.Name}' failed to compile: {result.Error}");
                }
            }
            if (again) Prepare(feature);
        }

        private static void LoadEffect(RenderFeature feature, string compiledPath)
        {
            if (_services == null)
            {
                SetStatus(feature, RenderFeatureStatus.Error, "The renderer isn't ready.");
                return;
            }

            var content = new FileContentManager(_services, Path.GetDirectoryName(compiledPath));
            try
            {
                Effect effect = content.Load<Effect>(Path.GetFileNameWithoutExtension(compiledPath));
                UnloadEffect(feature);
                feature.EffectContent = content;
                feature.Effect = effect;
                ApplyTechnique(feature);
            }
            catch (Exception e)
            {
                content.Dispose();
                SetStatus(feature, RenderFeatureStatus.Error, "Couldn't load the compiled shader: " + e.Message);
            }
        }

        private static void ApplyTechnique(RenderFeature feature)
        {
            Effect effect = feature.Effect;
            if (effect == null) return;
            feature.ParameterCache.Clear();

            EffectTechnique technique = string.IsNullOrEmpty(feature.Technique) ? null : effect.Techniques[feature.Technique];
            if (technique == null && !string.IsNullOrEmpty(feature.Technique))
            {
                effect.CurrentTechnique = effect.Techniques[0];
                SetStatus(feature, RenderFeatureStatus.Error,
                    $"Technique '{feature.Technique}' not found; running '{effect.Techniques[0].Name}'.");
                return;
            }
            effect.CurrentTechnique = technique ?? effect.Techniques[0];
            SetStatus(feature, RenderFeatureStatus.Ready, null);
        }

        private static void UnloadEffect(RenderFeature feature)
        {
            feature.ParameterCache.Clear();
            feature.Effect = null;
            feature.EffectContent?.Dispose();
            feature.EffectContent = null;
        }

        /// <summary>A feature threw while drawing: it stops drawing until it is compiled again.</summary>
        internal static void ReportDrawError(RenderFeature feature, string error)
        {
            UnloadEffect(feature);
            SetStatus(feature, RenderFeatureStatus.Error, error);
        }

        private static void SetStatus(RenderFeature feature, RenderFeatureStatus status, string error)
        {
            feature.Status = status;
            feature.Error = error;
            Publish();
        }

        /// <summary>
        /// Per frame (game thread): applies finished compiles and recompiles shaders saved since. Called by
        /// <see cref="Logic.ScreenManager.Update"/>.
        /// </summary>
        public static void Update()
        {
            while (_completed.TryDequeue(out Action action))
            {
                try { action(); }
                catch (Exception e) { EditorBridge.Log("RenderFeatures: applying a compile threw: " + e); }
            }

            if (++_frame % SourcePollFrames != 0) return;
            foreach (RenderFeature feature in _features)
            {
                if (_compiling.Contains(feature.Id)) continue;
                string source = ShaderSourcePath(feature);
                if (source == null || !File.Exists(source)) continue;
                if (File.GetLastWriteTimeUtc(source) != feature.CompiledSourceTimeUtc) StartCompile(feature);
            }
        }

        #endregion

        #region Editing (game thread)

        /// <summary>
        /// Adds a feature with a new shader from the template (<c>Content/RenderFeatures/&lt;Name&gt;.fx</c>),
        /// saves and compiles it. Returns the feature.
        /// </summary>
        public static RenderFeature Create(string name, RenderFeatureEvent at)
        {
            name = UniqueName(string.IsNullOrWhiteSpace(name) ? "New Render Feature" : name.Trim());
            string shader = UniqueShaderPath(name);
            string path = Path.Combine(SourceRoot, shader.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, RenderFeatureShaderTemplate.For(name));

            var feature = new RenderFeature { Name = name, Event = at, Shader = shader };
            feature.Normalize();
            int insertAt = _features.FindLastIndex(f => f.Event <= at) + 1;
            _features.Insert(insertAt, feature);
            Save();
            StartCompile(feature);
            return feature;
        }

        /// <summary>
        /// Removes a feature and saves. With <paramref name="deleteShader"/> its .fx and compiled .xnb go through
        /// <paramref name="deleteFile"/> (e.g. Anvil's Recycle Bin), or are deleted when it is null.
        /// </summary>
        public static bool Remove(Guid id, bool deleteShader, Action<string> deleteFile = null)
        {
            RenderFeature feature = _features.FirstOrDefault(f => f.Id == id);
            if (feature == null) return false;
            UnloadEffect(feature);
            _features.Remove(feature);
            _compileAgain.Remove(id);
            Save();

            // Another feature may share the shader.
            if (deleteShader && !string.IsNullOrEmpty(feature.Shader) && _features.All(f => f.Shader != feature.Shader))
            {
                foreach (string file in new[] { ShaderSourcePath(feature), RenderFeatureCompiler.CompiledPath(SourceRoot, feature.Shader) })
                {
                    try
                    {
                        if (!File.Exists(file)) continue;
                        if (deleteFile != null) deleteFile(file);
                        else File.Delete(file);
                    }
                    catch (Exception e) { EditorBridge.Log($"RenderFeatures: couldn't delete '{file}': {e.Message}"); }
                }
            }
            Publish();
            return true;
        }

        /// <summary>Edits a feature's settings and saves. A changed Shader or Technique reloads it.</summary>
        public static void Mutate(Guid id, Action<RenderFeature> mutate)
        {
            RenderFeature feature = _features.FirstOrDefault(f => f.Id == id);
            if (feature == null || mutate == null) return;
            string shader = feature.Shader, technique = feature.Technique;
            mutate(feature);
            feature.Normalize();
            if (feature.Shader != shader)
            {
                UnloadEffect(feature);
                Prepare(feature);
            }
            else if (feature.Technique != technique) ApplyTechnique(feature);
            Save();
            Publish();
        }

        /// <summary>Compiles a feature's shader again now.</summary>
        public static void Recompile(Guid id)
        {
            RenderFeature feature = _features.FirstOrDefault(f => f.Id == id);
            if (feature == null) return;
            if (string.IsNullOrEmpty(feature.Shader) || !File.Exists(ShaderSourcePath(feature)))
                SetStatus(feature, RenderFeatureStatus.Error, "Shader file not found: " + feature.Shader);
            else StartCompile(feature);
        }

        /// <summary>
        /// Moves a feature one row up (-1) or down (+1) in the pipeline: past the neighbouring feature, or across the
        /// neighbouring built-in passes into the previous/next injection point. Saves. False when it can't move.
        /// </summary>
        public static bool Move(Guid id, int direction)
        {
            RenderFeature feature = _features.FirstOrDefault(f => f.Id == id);
            if (feature == null || direction == 0) return false;
            direction = Math.Sign(direction);

            List<RenderFeature> slot = _features.Where(f => f.Event == feature.Event).ToList();
            int inSlot = slot.IndexOf(feature);
            int neighbour = inSlot + direction;
            if (neighbour >= 0 && neighbour < slot.Count)
            {
                // Swap with the neighbour in the same injection point.
                int a = _features.IndexOf(feature), b = _features.IndexOf(slot[neighbour]);
                (_features[a], _features[b]) = (_features[b], _features[a]);
            }
            else
            {
                int target = (int)feature.Event + direction;
                if (!Enum.IsDefined(typeof(RenderFeatureEvent), target)) return false;
                feature.Event = (RenderFeatureEvent)target;
                _features.Remove(feature);
                // Last of the previous point when moving up, first of the next one when moving down.
                if (direction < 0) _features.Insert(_features.FindLastIndex(f => f.Event <= feature.Event) + 1, feature);
                else
                {
                    int first = _features.FindIndex(f => f.Event >= feature.Event);
                    _features.Insert(first < 0 ? _features.Count : first, feature);
                }
            }
            Save();
            Publish();
            return true;
        }

        /// <summary>Puts back the saved values over changes scripts made during Play (Stop).</summary>
        public static void RestoreSaved()
        {
            bool changed = false;
            foreach (RenderFeature feature in _features)
            {
                if (!_saved.TryGetValue(feature.Id, out RenderFeature saved)) continue;
                string technique = feature.Technique;
                feature.CopySettingsFrom(saved);
                if (feature.Technique != technique) ApplyTechnique(feature);
                changed = true;
            }
            if (changed) Publish();
        }

        /// <summary>Rebuilds <see cref="Describe"/> (render setting toggles don't notify).</summary>
        public static void Refresh() => Publish();

        private static string UniqueName(string name)
        {
            string candidate = name;
            for (int i = 2; _features.Any(f => string.Equals(f.Name, candidate, StringComparison.OrdinalIgnoreCase)); i++)
                candidate = $"{name} {i}";
            return candidate;
        }

        private static string UniqueShaderPath(string name)
        {
            var sb = new StringBuilder();
            foreach (char c in name)
                if (char.IsLetterOrDigit(c) || c == '_') sb.Append(c);
            string stem = sb.Length == 0 ? "RenderFeature" : sb.ToString();

            string folder = Path.Combine(SourceRoot, ShaderFolder);
            string candidate = stem;
            for (int i = 2; File.Exists(Path.Combine(folder, candidate + ".fx")) ||
                            _features.Any(f => string.Equals(f.Shader, $"{ShaderFolder}/{candidate}.fx", StringComparison.OrdinalIgnoreCase)); i++)
                candidate = stem + i;
            return $"{ShaderFolder}/{candidate}.fx";
        }

        #endregion

        #region Pipeline description

        private static void Publish()
        {
            var layers = new List<RenderPipelineLayer>();
            void Stage(string name, string detail, bool active) =>
                layers.Add(new RenderPipelineLayer { Kind = RenderPipelineLayerKind.Stage, Name = name, Detail = detail, Active = active });
            void Point(RenderFeatureEvent at, string name, string detail)
            {
                layers.Add(new RenderPipelineLayer { Kind = RenderPipelineLayerKind.InjectionPoint, Name = name, Detail = detail, Active = true, Event = at });
                foreach (RenderFeature feature in _features)
                    if (feature.Event == at)
                        layers.Add(new RenderPipelineLayer
                        {
                            Kind = RenderPipelineLayerKind.Feature,
                            Name = feature.Name,
                            Detail = feature.Shader,
                            Active = feature.Enabled,
                            Event = at,
                            Feature = feature.CloneSettings(),
                        });
            }

            Stage("Shadow Maps", "Directional, point and spot light shadows", true);
            Stage("G-Buffer", "Opaque geometry: albedo, normals, depth", true);
            Stage("Decals", "Deferred decals", GameSettings.g_drawdecals);
            Stage("Screen-Space Reflections", "SSR", GameSettings.g_SSReflection);
            Stage("Ambient Occlusion", "SSAO", GameSettings.g_ssao_draw);
            Stage("Screen-Space Shadows", "Directional light shadows + blur", true);
            Stage("Volumetric Fog", "Froxel fog and god rays", GameSettings.g_FroxelsEnabled);
            Stage("Deferred Lighting", "Point, spot and directional lights", true);
            Stage("Environment Lighting", "Sky and environment map", GameSettings.g_environmentmapping);
            Stage("Deferred Compose", "Albedo x lighting, fog", true);
            Point(RenderFeatureEvent.AfterOpaques, "After Opaques", "HDR, before transparents");
            Stage("Forward", "Transparent and special materials", GameSettings.g_forwardenable);
            Stage("Water", "Water surfaces and splash rings", true);
            Stage("Particles", "Particle System components", true);
            Stage("Weather", "Rain, snow, sandstorm", true);
            Point(RenderFeatureEvent.AfterTransparents, "After Transparents", "HDR, before TAA and bloom");
            Stage("Temporal AA", "TAA", GameSettings.g_taa);
            Stage("Bloom", "Bloom", GameSettings.g_BloomEnable);
            Stage("Auto Exposure", "Eye adaptation", GameSettings.g_AutoExposure);
            Point(RenderFeatureEvent.AfterBloom, "After Bloom", "HDR, before tonemapping");
            Stage("Post Processing", "Exposure, tonemap, vignette, chromatic aberration", GameSettings.g_PostProcessing);
            Stage("Color Grading", "LUT", GameSettings.g_PostProcessing && GameSettings.g_ColorGrading);
            Point(RenderFeatureEvent.AfterPostProcessing, "After Post Processing", "Final LDR image");
            Stage("Editor Overlays", "Grid, outlines, gizmos (edit mode)", GameSettings.e_enableeditor);
            Stage("Game UI", "Vista menus and HUDs, debug stats", true);

            _published = layers;
            try { Changed?.Invoke(); }
            catch (Exception e) { EditorBridge.Log("RenderFeatures.Changed handler threw: " + e); }
        }

        #endregion

        // Loads .xnb files from any folder: TitleContainer only opens files inside the game's folder.
        private sealed class FileContentManager : ContentManager
        {
            public FileContentManager(IServiceProvider services, string folder) : base(services, folder) { }

            protected override Stream OpenStream(string assetName) =>
                File.OpenRead(Path.Combine(RootDirectory, assetName + ".xnb"));
        }
    }
}
