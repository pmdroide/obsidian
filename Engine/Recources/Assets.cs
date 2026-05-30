using System;
using System.Collections.Generic;
using Engine.Editor;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Recources
{
    public class Assets : IDisposable
    {
        ////////////////////////////////////////////////////////////////////////////////////////////////////////////
        //  VARIABLES
        ////////////////////////////////////////////////////////////////////////////////////////////////////////////

        //Default Meshes + Editor

        public Model EditorArrow;
        public Model EditorArrowRound;

        public Model Sphere;
        public ModelMeshPart SphereMeshPart;
        public ModelDefinition IsoSphere;

        public ModelDefinition Plane;

        public ModelDefinition Cube;

        //https://sketchfab.com/models/95c4008c4c764c078f679d4c320e7b18
        public ModelDefinition Tiger;

        public ModelDefinition HumanModel;

        public Texture2D IconLight;
        public Texture2D IconEnvmap;
        public Texture2D IconDecal;

        //Default Materials

        public MaterialEffect MaterialSSS_Red;
        public MaterialEffect MaterialSSS_Green;
        public MaterialEffect MaterialSSS_Cyan;
        public MaterialEffect BaseMaterial;
        public MaterialEffect BaseMaterialGray;
        public MaterialEffect GoldMaterial;
        public MaterialEffect EmissiveMaterial;
        public MaterialEffect EmissiveMaterial2;
        public MaterialEffect EmissiveMaterial3;
        public MaterialEffect EmissiveMaterial4;
        public MaterialEffect SilverMaterial;
        public MaterialEffect HologramMaterial;
        public MaterialEffect MetalRough03Material;
        public MaterialEffect AlphaBlendRim;
        public MaterialEffect MirrorMaterial;

        //Shader stuff

        public Texture2D NoiseMap;

        public TextureCube SkyCubemap;
        public Texture2D SkyTexture;

        public static Texture2D BaseTex;

        //Meshes and Materials

        //public Model Trabant;
        //public MaterialEffect TrabantBigParts;

        public ModelDefinition SponzaModel;
        readonly List<Texture2D> _sponzaTextures = new List<Texture2D>();
        private Texture2D sponza_fabric_metallic;
        private Texture2D sponza_fabric_spec;
        private Texture2D sponza_curtain_metallic;

        public Model SkullModel;

        public Model HelmetModel;

        public ModelDefinition StanfordDragon;
        public ModelDefinition StanfordDragonLowpoly;

        public MaterialEffect RockMaterial;


        public SpriteFont DefaultFont;
        public SpriteFont MonospaceFont;
        
        public MaterialEffect DragonLowPolyMaterial;

        // -------- Error / fallback assets --------

        // Shown when an imported model fails to build/load, and used as the default
        // albedo for runtime-imported models (the deferred renderer draws each entity
        // with a single MaterialEffect, not the FBX's embedded maps).
        public ModelDefinition ErrorModel;
        public Texture2D ErrorTexture;
        public MaterialEffect ErrorMaterial;

        // -------- Runtime-registered models (added via AssetImporter at editor runtime) --------

        // Hot-loaded models live here keyed by their sanitised name. EditorBridge.BuildModelKeys
        // unions this with the reflection-scanned public ModelDefinition fields, so the editor's
        // model-picker treats hard-coded and imported models uniformly.
        private readonly Dictionary<string, ModelDefinition> _dynamicModels =
            new Dictionary<string, ModelDefinition>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyDictionary<string, ModelDefinition> DynamicModels => _dynamicModels;

        public event Action<string, ModelDefinition> ModelRegistered;

        // -------- Per-model materials bound from convention-named textures --------

        // Keyed by the same model key as _dynamicModels. Populated by AssetImporter when
        // the user drops textures (e.g. {key}_BaseColor.png) into a model's Textures folder.
        // EnqueueAddBasicEntity prefers this over BaseMaterial/ErrorMaterial so imported
        // models render with their bound textures.
        private readonly Dictionary<string, MaterialEffect> _dynamicMaterials =
            new Dictionary<string, MaterialEffect>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyDictionary<string, MaterialEffect> DynamicMaterials => _dynamicMaterials;

        public event Action<string> MaterialRegistered;

        // Captured by Load() so AssetImporter can re-use the live ContentManager / GraphicsDevice
        // for new XNBs instead of constructing a parallel content stack.
        public ContentManager Content { get; private set; }
        public GraphicsDevice GraphicsDevice { get; private set; }

        ////////////////////////////////////////////////////////////////////////////////////////////////////////////
        //  FUNCTIONS
        ////////////////////////////////////////////////////////////////////////////////////////////////////////////

        public void Load(ContentManager content, GraphicsDevice graphicsDevice)
        {
            Content = content;
            GraphicsDevice = graphicsDevice;
            //Default Meshes + Editor
            EditorArrow = content.Load<Model>("Art/Editor/Arrow");
            EditorArrowRound = content.Load<Model>("Art/Editor/ArrowRound");

            IsoSphere = new ModelDefinition(content, "Art/default/isosphere", graphicsDevice, true, new Vector3(50, 50, 50));
            
            Sphere = content.Load<Model>("Art/default/sphere");
            SphereMeshPart = Sphere.Meshes[0].MeshParts[0];

            Plane = new ModelDefinition(content, "Art/Plane", graphicsDevice);

            Cube = new ModelDefinition(content, "Art/test/cube", graphicsDevice, true, new Vector3(50, 50, 50));

            Tiger = new ModelDefinition(content, "Art/Tiger/Tiger", graphicsDevice, true, new Vector3(50,50,50));
            HumanModel = new ModelDefinition(content, "Art/Human/human", graphicsDevice, true, new Vector3(50, 50, 50));

            IconDecal = content.Load<Texture2D>("Art/Editor/icon_decal");
            IconLight = content.Load<Texture2D>("Art/Editor/icon_light");
            IconEnvmap = content.Load<Texture2D>("Art/Editor/icon_envmap");
            //Default Materials

            BaseMaterial = CreateMaterial(Color.Red, 0.5f, 0, type: MaterialEffect.MaterialTypes.Basic);

            MaterialSSS_Red = CreateMaterial(Color.Red, 0.5f, 0, type: MaterialEffect.MaterialTypes.SubsurfaceScattering);
            MaterialSSS_Green = CreateMaterial(Color.Lime, 0.5f, 0, type: MaterialEffect.MaterialTypes.Basic);
            MaterialSSS_Cyan = CreateMaterial(Color.Cyan, 0.5f, 0, type: MaterialEffect.MaterialTypes.SubsurfaceScattering);

            BaseMaterialGray = CreateMaterial(Color.LightGray, 0.8f, 0, type: MaterialEffect.MaterialTypes.Basic);

            MetalRough03Material = CreateMaterial(Color.Silver, 0.2f, 1);
            AlphaBlendRim = CreateMaterial(Color.Silver, 0.05f, 1, type: MaterialEffect.MaterialTypes.ForwardShaded);
            MirrorMaterial = CreateMaterial(Color.White, 0.05f, 1);

            HologramMaterial = CreateMaterial(Color.White, 0.2f, 1, null, null, null, null, null, null, MaterialEffect.MaterialTypes.Hologram, 1);

            EmissiveMaterial = CreateMaterial(Color.White, 0.2f, 1, null, null, null, null, null, null, MaterialEffect.MaterialTypes.Emissive, 1.5f);

            EmissiveMaterial2 = CreateMaterial(Color.MonoGameOrange, 0.2f, 1, null, null, null, null, null, null, MaterialEffect.MaterialTypes.Emissive, 1.8f);
            EmissiveMaterial3 = CreateMaterial(Color.Violet, 0.2f, 1, null, null, null, null, null, null, MaterialEffect.MaterialTypes.Emissive, 1.8f);
            EmissiveMaterial4 = CreateMaterial(Color.LimeGreen, 0.2f, 1, null, null, null, null, null, null, MaterialEffect.MaterialTypes.Emissive, 1.8f);

            GoldMaterial = CreateMaterial(Color.Gold, 0.2f, 1);

            SilverMaterial = CreateMaterial(Color.Silver, 0.05f, 1);

            //Shader stuff

            BaseTex = new Texture2D(graphicsDevice, 1, 1);
            BaseTex.SetData(new Color[] { Color.White });

            NoiseMap = content.Load<Texture2D>("Shaders/noise_blur");

            // Try loading sky as cubemap first, then as 2D lat-long texture.
            try
            {
                SkyCubemap = content.Load<TextureCube>("Art/sky");
                SkyTexture = null;
            }
            catch
            {
                SkyCubemap = null;

                try
                {
                    SkyTexture = content.Load<Texture2D>("Art/sky");
                }
                catch
                {
                    SkyTexture = null;
                }
            }
            //Meshes and Materials

            //Trabant = content.Load<Model>("Art/test/source/trabant_realtime_v3");

            //TrabantBigParts = CreateMaterial(Color.White, roughness: 1, metallic: 0,
            //    albedoMap: content.Load<Texture2D>("Art/test/textures/big_parts_col"),
            //    normalMap: content.Load<Texture2D>("Art/test/textures/big_parts_nor"),
            //    roughnessMap: content.Load<Texture2D>("Art/test/textures/big_parts_rough"));

            //MaterialEffect TrabantWindow = CreateMaterial(Color.White, roughness: 0.04f, metallic: 0.5f);

            //MaterialEffect TrabantSmallParts = CreateMaterial(Color.White, roughness: 1, metallic: 0,
            //    albedoMap: content.Load<Texture2D>("Art/test/textures/small_parts_col"),
            //    normalMap: null,
            //    roughnessMap: content.Load<Texture2D>("Art/test/textures/small_parts_rough"));

            //Trabant.Meshes[0].MeshParts[0].Effect = TrabantWindow;
            //Trabant.Meshes[1].MeshParts[0].Effect = TrabantBigParts;
            //Trabant.Meshes[3].MeshParts[0].Effect = TrabantSmallParts;

            //

            StanfordDragon = new ModelDefinition(content, "Art/default/dragon_uv_smooth", graphicsDevice, false, new Vector3(70, 70, 70)); 
            StanfordDragonLowpoly = new ModelDefinition(content, "Art/default/dragon_lowpoly", graphicsDevice, true, new Vector3(60, 60,60));

            DragonLowPolyMaterial = CreateMaterial(Color.Red, 0.5f, 0, type: MaterialEffect.MaterialTypes.Basic, normalMap: content.Load<Texture2D>("Art/default/dragon_normal"));

            HelmetModel = content.Load<Model>("Art/default/daft_helmets");
            SkullModel = content.Load<Model>("Art/default/skull");

            //

            SponzaModel = new ModelDefinition(content, "Sponza/Sponza", graphicsDevice, false);
            _sponzaTextures.Add(content.Load<Texture2D>("Sponza/textures/background_ddn"));
            _sponzaTextures.Add( content.Load<Texture2D>("Sponza/textures/chain_texture_ddn"));
            _sponzaTextures.Add( content.Load<Texture2D>("Sponza/textures/chain_texture_mask"));
            _sponzaTextures.Add( content.Load<Texture2D>("Sponza/textures/lion_ddn"));
            _sponzaTextures.Add( content.Load<Texture2D>("Sponza/textures/lion2_ddn"));
            _sponzaTextures.Add( content.Load<Texture2D>("Sponza/textures/spnza_bricks_a_ddn"));
            _sponzaTextures.Add( content.Load<Texture2D>("Sponza/textures/spnza_bricks_a_spec"));
            _sponzaTextures.Add( content.Load<Texture2D>("Sponza/textures/sponza_arch_ddn"));
            _sponzaTextures.Add( content.Load<Texture2D>("Sponza/textures/sponza_arch_spec"));
            _sponzaTextures.Add( content.Load<Texture2D>("Sponza/textures/sponza_ceiling_a_spec"));
            _sponzaTextures.Add( content.Load<Texture2D>("Sponza/textures/sponza_column_a_ddn"));
            _sponzaTextures.Add( content.Load<Texture2D>("Sponza/textures/sponza_column_a_spec"));
            _sponzaTextures.Add( content.Load<Texture2D>("Sponza/textures/sponza_column_b_spec"));
            _sponzaTextures.Add( content.Load<Texture2D>("Sponza/textures/sponza_column_b_ddn"));
            _sponzaTextures.Add( content.Load<Texture2D>("Sponza/textures/sponza_column_c_spec"));
            _sponzaTextures.Add( content.Load<Texture2D>("Sponza/textures/sponza_column_c_ddn"));
            _sponzaTextures.Add(sponza_fabric_spec = content.Load<Texture2D>("Sponza/textures/sponza_fabric_spec"));
            _sponzaTextures.Add(sponza_fabric_metallic = content.Load<Texture2D>("Sponza/textures/sponza_fabric_metallic"));
            _sponzaTextures.Add(content.Load<Texture2D>("Sponza/textures/sponza_curtain_green_spec"));
            _sponzaTextures.Add(content.Load<Texture2D>("Sponza/textures/sponza_curtain_blue_spec"));
            _sponzaTextures.Add(content.Load<Texture2D>("Sponza/textures/sponza_curtain_spec"));
            _sponzaTextures.Add( content.Load<Texture2D>("Sponza/textures/sponza_details_spec"));
            _sponzaTextures.Add( content.Load<Texture2D>("Sponza/textures/sponza_flagpole_spec"));
            _sponzaTextures.Add( content.Load<Texture2D>("Sponza/textures/sponza_thorn_ddn"));
            _sponzaTextures.Add( content.Load<Texture2D>("Sponza/textures/sponza_thorn_mask"));
            _sponzaTextures.Add( content.Load<Texture2D>("Sponza/textures/sponza_thorn_spec"));
            _sponzaTextures.Add( content.Load<Texture2D>("Sponza/textures/vase_ddn"));
            _sponzaTextures.Add( content.Load<Texture2D>("Sponza/textures/vase_plant_mask"));
            _sponzaTextures.Add( content.Load<Texture2D>("Sponza/textures/vase_plant_spec"));
            _sponzaTextures.Add( content.Load<Texture2D>("Sponza/textures/vase_round_ddn"));
            _sponzaTextures.Add( content.Load<Texture2D>("Sponza/textures/vase_round_spec"));

            _sponzaTextures.Add(content.Load<Texture2D>("Sponza/textures/sponza_floor_a_spec"));
            _sponzaTextures.Add(content.Load<Texture2D>("Sponza/textures/sponza_floor_a_ddn"));
            
            sponza_curtain_metallic = content.Load<Texture2D>("Sponza/textures/sponza_curtain_metallic");

            ProcessSponza();
            
            ProcessHelmets();

            RockMaterial = CreateMaterial(Color.White, roughness: 1, metallic: 0,
                albedoMap: content.Load<Texture2D>("Art/test/squarebricks-diffuse"),
                normalMap: content.Load<Texture2D>("Art/test/squarebricks-normal"),
                roughnessMap: null,
                metallicMap: null,
                mask: null,
                displacementMap: content.Load<Texture2D>("Art/test/squarebricks-depth")
            );

            //Fonts

            DefaultFont = content.Load<SpriteFont>("Fonts/defaultFont");
            MonospaceFont = content.Load<SpriteFont>("Fonts/monospace");

            // Error / fallback assets. Wrapped so a missing error asset never blocks boot.
            // ErrorModel is the "ERROR" text mesh shown when an import fails to build/load;
            // ErrorTexture/ErrorMaterial (below) is the default albedo applied to imported
            // models. Falls back to the Cube primitive if the error mesh can't load.
            try
            {
                ErrorModel = new ModelDefinition(content, "Art/Error/ERRORText", graphicsDevice);
                // Bind the mesh's own embedded textures so the error model shows them when
                // spawned with a null material (TODO: "Error model doesn't display textures").
                BindEmbeddedTextures(ErrorModel.Model);
            }
            catch
            {
                ErrorModel = Cube; // already loaded above; guaranteed-valid fallback geometry.
            }
            try
            {
                ErrorTexture = content.Load<Texture2D>("Art/error");
                ErrorMaterial = CreateMaterial(Color.White, 0.6f, 0, albedoMap: ErrorTexture);
            }
            catch (Exception ex)
            {
                ErrorTexture = null;
                ErrorMaterial = CreateMaterial(Color.Magenta, 0.6f, 0);
                EditorBridge.Log("Assets: failed to load Art/error texture: " + ex.Message);
            }

            // Re-register models imported in previous editor sessions so they reappear
            // in the Meshes folder and saved scenes referencing them can load.
            ReimportExistingModels(content, graphicsDevice);
        }

        /// <summary>
        /// Scans the built content directory for previously imported models (written by
        /// <see cref="AssetImporter"/> under Art/Models/{key}/{key}.xnb) and registers each
        /// into the dynamic-model registry. Failures per-model are logged and skipped so a
        /// single bad asset never blocks startup.
        /// </summary>
        private void ReimportExistingModels(ContentManager content, GraphicsDevice graphicsDevice)
        {
            try
            {
                string modelsDir = System.IO.Path.Combine(
                    AppContext.BaseDirectory, content.RootDirectory, "Art", "Models");
                if (!System.IO.Directory.Exists(modelsDir)) return;

                foreach (string dir in System.IO.Directory.EnumerateDirectories(modelsDir))
                {
                    string key = System.IO.Path.GetFileName(dir);
                    if (string.IsNullOrEmpty(key)) continue;
                    if (_dynamicModels.ContainsKey(key) || IsKeyTaken(key)) continue;

                    string xnb = System.IO.Path.Combine(dir, key + ".xnb");
                    if (!System.IO.File.Exists(xnb)) continue;

                    try
                    {
                        var md = new ModelDefinition(content, $"Art/Models/{key}/{key}", graphicsDevice);
                        RegisterModel(key, md);
                        // Re-bind textures dropped in a previous session so the model keeps
                        // its appearance across restarts (matches the import-time binding).
                        TryBindStoredTextures(content, key);
                        EditorBridge.Log($"Assets: re-registered imported model '{key}'");
                    }
                    catch (Exception ex)
                    {
                        EditorBridge.Log($"Assets: failed to re-register model '{key}': " + ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                EditorBridge.Log("Assets: ReimportExistingModels failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Scans a model's built <c>Art/Models/{key}/Textures/</c> folder for convention-named
        /// textures and re-binds a material, so textures dropped in a previous session survive
        /// a restart. Mirrors <see cref="AssetImporter"/>'s convention binding. No-op when the
        /// folder is empty or no albedo is present (the model keeps the error material).
        /// </summary>
        private void TryBindStoredTextures(ContentManager content, string key)
        {
            try
            {
                string texDir = System.IO.Path.Combine(
                    AppContext.BaseDirectory, content.RootDirectory, "Art", "Models", key, "Textures");
                if (!System.IO.Directory.Exists(texDir)) return;

                Texture2D albedo = null, normal = null, rough = null, metallic = null, mask = null, disp = null;
                foreach (string xnb in System.IO.Directory.EnumerateFiles(texDir, "*.xnb"))
                {
                    string stem = System.IO.Path.GetFileNameWithoutExtension(xnb);
                    AssetImporter.MaterialUsage usage = AssetImporter.ClassifyTexture(stem);
                    if (usage == AssetImporter.MaterialUsage.None) continue;

                    Texture2D tex;
                    try { tex = content.Load<Texture2D>($"Art/Models/{key}/Textures/{stem}"); }
                    catch { continue; }

                    switch (usage)
                    {
                        case AssetImporter.MaterialUsage.Albedo: albedo = tex; break;
                        case AssetImporter.MaterialUsage.Normal: normal = tex; break;
                        case AssetImporter.MaterialUsage.Roughness: rough = tex; break;
                        case AssetImporter.MaterialUsage.Metallic: metallic = tex; break;
                        case AssetImporter.MaterialUsage.Mask: mask = tex; break;
                        case AssetImporter.MaterialUsage.Displacement: disp = tex; break;
                    }
                }

                if (albedo == null) return;
                MaterialEffect mat = CreateMaterial(Color.White, roughness: 1f, metallic: 0f,
                    albedoMap: albedo, normalMap: normal, roughnessMap: rough, metallicMap: metallic,
                    mask: mask, displacementMap: disp);
                RegisterMaterial(key, mat);
                EditorBridge.Log($"Assets: re-bound stored textures for '{key}'");
            }
            catch (Exception ex)
            {
                EditorBridge.Log($"Assets: TryBindStoredTextures('{key}') failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Register a runtime-imported model under a unique key. If the requested key is
        /// already taken (by a hard-coded field or another dynamic entry), the key is
        /// suffixed with _2, _3, … until unique. Fires <see cref="ModelRegistered"/> so
        /// the bridge can refresh its model-key index. Returns the actual key used.
        /// </summary>
        public string RegisterModel(string requestedKey, ModelDefinition model)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            string key = string.IsNullOrWhiteSpace(requestedKey) ? "model" : requestedKey;
            string final = key;
            int n = 2;
            while (IsKeyTaken(final)) final = key + "_" + (n++);
            _dynamicModels[final] = model;
            ModelRegistered?.Invoke(final, model);
            return final;
        }

        private bool IsKeyTaken(string key)
        {
            if (_dynamicModels.ContainsKey(key)) return true;
            // Also check hard-coded public ModelDefinition fields, so dynamic imports
            // never shadow a built-in (e.g. someone dropping a file literally named
            // "SponzaModel.fbx" doesn't replace the hard-coded reference).
            var f = typeof(Assets).GetField(key, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            return f != null && f.FieldType == typeof(ModelDefinition);
        }

        /// <summary>
        /// Store (or replace) the material bound to a model key from convention-named
        /// textures. Fires <see cref="MaterialRegistered"/> so the bridge can refresh
        /// already-placed instances. Pass null to clear a previous binding.
        /// </summary>
        public void RegisterMaterial(string key, MaterialEffect material)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            if (material == null) _dynamicMaterials.Remove(key);
            else _dynamicMaterials[key] = material;
            MaterialRegistered?.Invoke(key);
        }

        public bool TryGetDynamicMaterial(string key, out MaterialEffect material)
        {
            material = null;
            return !string.IsNullOrEmpty(key) && _dynamicMaterials.TryGetValue(key, out material) && material != null;
        }

        /// <summary>
        /// Remove a runtime-imported model (and any material bound to it) from the
        /// dynamic registries. Used when the user deletes a model from the Assets panel.
        /// Built-in (hard-coded) models are never removable and are ignored here.
        /// Returns true if a dynamic model was removed.
        /// </summary>
        public bool UnregisterModel(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            _dynamicMaterials.Remove(key);
            bool removed = _dynamicModels.Remove(key);
            // Reuse ModelRegistered to trigger a model-key rebuild on the bridge side.
            if (removed) ModelRegistered?.Invoke(key, null);
            return removed;
        }

        /// <summary>
        /// Public material factory for the runtime importer — routes through the same
        /// <see cref="CreateMaterial"/> path the built-ins use (so the shared
        /// <see cref="Shaders.DeferredClear"/> effect backs every material).
        /// </summary>
        public MaterialEffect MakeMaterial(Color color, float roughness, float metallic,
            Texture2D albedoMap = null, Texture2D normalMap = null, Texture2D roughnessMap = null,
            Texture2D metallicMap = null, Texture2D mask = null, Texture2D displacementMap = null,
            MaterialEffect.MaterialTypes type = 0, float emissiveStrength = 0)
        {
            return CreateMaterial(color, roughness, metallic, albedoMap, normalMap, roughnessMap,
                metallicMap, mask, displacementMap, type, emissiveStrength);
        }

        /// <summary>
        /// Converts a model's embedded FBX materials (BasicEffect + its texture) into
        /// engine <see cref="MaterialEffect"/>s per mesh-part, so a model dragged into the
        /// scene with a null material renders with the textures the FBX shipped. Tolerant
        /// variant of <see cref="ProcessModel"/>: mesh-parts that aren't BasicEffect (or
        /// are already MaterialEffect) are left as-is instead of throwing.
        /// </summary>
        public void BindEmbeddedTextures(Model model)
        {
            if (model == null) return;
            foreach (ModelMesh mesh in model.Meshes)
            {
                foreach (ModelMeshPart meshPart in mesh.MeshParts)
                {
                    if (meshPart.Effect is MaterialEffect) continue; // already processed
                    if (!(meshPart.Effect is BasicEffect oEffect)) continue;

                    MaterialEffect matEffect = new MaterialEffect(oEffect);
                    if (oEffect.TextureEnabled && oEffect.Texture != null)
                        matEffect.AlbedoMap = oEffect.Texture;
                    matEffect.DiffuseColor = oEffect.DiffuseColor;
                    meshPart.Effect = matEffect;
                }
            }
        }

        /// <summary>
        /// Create custom materials, you can add certain maps like Albedo, normal, etc. if you like.
        /// </summary>
        /// <param name="color"></param>
        /// <param name="roughness"></param>
        /// <param name="metallic"></param>
        /// <param name="albedoMap"></param>
        /// <param name="normalMap"></param>
        /// <param name="roughnessMap"></param>
        /// <param name="metallicMap"></param>
        /// <param name="mask"></param>
        /// <param name="type">2: hologram, 3:emissive</param>
        /// <param name="emissiveStrength"></param>
        /// <returns></returns>
        private MaterialEffect CreateMaterial(Color color, float roughness, float metallic, Texture2D albedoMap = null, Texture2D normalMap = null, Texture2D roughnessMap = null, Texture2D metallicMap = null, Texture2D mask = null, Texture2D displacementMap = null, MaterialEffect.MaterialTypes type = 0, float emissiveStrength = 0)
        {
            MaterialEffect mat = new MaterialEffect(Shaders.DeferredClear);
            mat.Initialize(color, roughness, metallic, albedoMap, normalMap, roughnessMap, metallicMap, mask, displacementMap, type, emissiveStrength);
            return mat;
        }

        /// <summary>
        /// The helmets have many submaterials and I want specific values for each one of them!
        /// </summary>
        private void ProcessHelmets()
        {
            for (int i = 0; i < HelmetModel.Meshes.Count; i++)
            {
                ModelMesh mesh = HelmetModel.Meshes[i];
                for (int index = 0; index < mesh.MeshParts.Count; index++)
                {
                    ModelMeshPart meshPart = mesh.MeshParts[index];
                    MaterialEffect matEffect = new MaterialEffect(meshPart.Effect);

                    matEffect.DiffuseColor = Color.Gray.ToVector3();

                    if (mesh.Name == "Helmet1_Interior")
                    {
                        matEffect.DiffuseColor = Color.White.ToVector3();
                    }

                    if (i == 5)
                    {
                        matEffect.DiffuseColor = new Color(0, 0.49f, 0.95f).ToVector3();
                        matEffect.Type = MaterialEffect.MaterialTypes.Hologram;
                    }

                    if (i == 0)
                    {
                        matEffect.DiffuseColor = Color.Black.ToVector3();
                        matEffect.Roughness = 0.1f;
                        matEffect.Type = MaterialEffect.MaterialTypes.ProjectHologram;
                    }

                    if (i == 1)
                    {
                        matEffect.DiffuseColor = new Color(0, 0.49f, 0.95f).ToVector3();
                    }

                    if (i == 2)
                    {
                        matEffect.DiffuseColor = Color.Silver.ToVector3();
                        matEffect.Metallic = 1;
                        matEffect.Roughness = 0.1f;
                    }

                    //Helmet color - should be gold!
                    if (i == 4)
                    {
                        matEffect.DiffuseColor = new Color(255, 255, 155).ToVector3() * 0.5f;
                        matEffect.Roughness = 0.3f;
                        matEffect.Metallic = 0.8f;
                    }

                    if (i == 13)
                    {
                        matEffect.DiffuseColor = Color.Black.ToVector3();
                        matEffect.Roughness = 0.05f;
                        matEffect.Type = MaterialEffect.MaterialTypes.ProjectHologram;
                    }

                    meshPart.Effect = matEffect;
                }
            }
        }
        
        private Model ProcessModel(Model model)
        {
            foreach (ModelMesh mesh in model.Meshes)
            {
                foreach (ModelMeshPart meshPart in mesh.MeshParts)
                {
                    MaterialEffect matEffect = new MaterialEffect(meshPart.Effect);

                    if (!(meshPart.Effect is BasicEffect))
                    {
                        throw new Exception("Can only process models with basic effect");
                    }

                    BasicEffect oEffect = meshPart.Effect as BasicEffect;

                    if (oEffect.TextureEnabled)
                        matEffect.AlbedoMap = oEffect.Texture;

                    matEffect.DiffuseColor = oEffect.DiffuseColor;

                    meshPart.Effect = matEffect;
                }
            }

            return model;
        }

        //Assign specific materials to submeshes
        private void ProcessSponza()
        {
            foreach (ModelMesh mesh in SponzaModel.Model.Meshes)
            {
                foreach (ModelMeshPart meshPart in mesh.MeshParts)
                {
                    MaterialEffect matEffect = new MaterialEffect(meshPart.Effect);

                    BasicEffect oEffect = meshPart.Effect as BasicEffect;

                    //I want to remove this mesh
                    if (mesh.Name == "g sponza_04")
                    {
                        //Put the boudning sphere into space?
                        mesh.BoundingSphere = new BoundingSphere(new Vector3(-100000, 0, 0), 0);

                        //Make it transparent
                        matEffect.IsTransparent = true;
                    }

                    matEffect.DiffuseColor = oEffect.DiffuseColor;

                    if (oEffect.TextureEnabled)
                    {
                        matEffect.AlbedoMap = oEffect.Texture;

                        string[] name = matEffect.AlbedoMap.Name.Split('\\');

                        string compare = name[2].Replace("_0", "");

                        if (compare.Contains("vase_round") || compare.Contains("vase_hanging"))
                        {
                            matEffect.Roughness = 0.1f;
                            matEffect.Metallic = 0.5f;
                        }

                        //Make the vases emissive!

                        //if (compare.Contains("vase_hanging"))
                        //{
                        //    matEffect.EmissiveStrength = 2;
                        //    matEffect.Type = MaterialEffect.MaterialTypes.Emissive;
                        //    matEffect.DiffuseColor = Color.Gold.ToVector3();

                        //    matEffect.AlbedoMap = null;
                        //    matEffect.HasDiffuse = false;
                        //}

                        //if (compare.Contains("floor"))
                        //{
                        //    matEffect.Roughness = 0.2f;
                        //    matEffect.Metallic = 1;
                        //    //matEffect.HasDiffuse = false;
                        //}


                        if (compare.Contains("chain"))
                        {
                            matEffect.Roughness = 0.5f;
                            matEffect.Metallic = 1f;
                        }

                        if (compare.Contains("curtain"))
                        {
                            matEffect.MetallicMap = sponza_curtain_metallic;
                        }

                        if (compare.Contains("sponza_fabric"))
                        {
                            matEffect.MetallicMap = sponza_fabric_metallic;
                            matEffect.RoughnessMap = sponza_fabric_spec;
                        }


                        if (compare.Contains("lion"))
                        {
                            matEffect.Metallic = 0.9f;
                        }

                        if (compare.Contains("_diff"))
                        {
                            compare = compare.Replace("_diff", "");
                        }

                        foreach (Texture2D tex2d in _sponzaTextures)
                        {
                            if (tex2d.Name.Contains(compare))
                            {
                                //We got a match!

                                string ending = tex2d.Name.Replace(compare, "");

                                ending = ending.Replace("Sponza/textures/", "");

                                if (ending == "_spec")
                                {
                                    matEffect.RoughnessMap = tex2d;
                                }

                                if (ending == "_metallic")
                                {
                                    matEffect.MetallicMap = tex2d;
                                }

                                if (ending == "_ddn")
                                {
                                    matEffect.NormalMap = tex2d;
                                }

                                if (ending == "_mask")
                                {
                                    matEffect.Mask = tex2d;
                                }

                            }
                        }


                    }
                    meshPart.Effect = matEffect;
                }


            }
        }

        public void Dispose()
        {
            IconLight?.Dispose();
            IconEnvmap?.Dispose();
            IconDecal?.Dispose();
            BaseMaterial?.Dispose();
            GoldMaterial?.Dispose();
            EmissiveMaterial?.Dispose();
            EmissiveMaterial2?.Dispose();
            EmissiveMaterial3?.Dispose();
            EmissiveMaterial4?.Dispose();
            SilverMaterial?.Dispose();
            HologramMaterial?.Dispose();
            MetalRough03Material?.Dispose();
            AlphaBlendRim?.Dispose();
            MirrorMaterial?.Dispose();
            NoiseMap?.Dispose();
            SkyTexture?.Dispose();
            sponza_fabric_metallic?.Dispose();
            sponza_fabric_spec?.Dispose();
            sponza_curtain_metallic?.Dispose();
            RockMaterial?.Dispose();
            ErrorMaterial?.Dispose();
            ErrorTexture?.Dispose();
        }
    }

}
