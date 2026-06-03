using System;
using System.Collections.Generic;
using BEPUphysics;
using BEPUphysics.BroadPhaseEntries;
using BEPUphysics.Entities;
using BEPUphysics.Entities.Prefabs;
using BEPUutilities;
using Engine.Editor;
using Engine.Entities;
using Engine.Recources;
using Engine.Recources.Helper;
using Engine.Renderer.Helper;
using Engine.Renderer.Helper.HelperGeometry;
using Engine.Renderer.RenderModules.Signed_Distance_Fields.SDF_Generator;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using DirectionalLight = Engine.Entities.DirectionalLight;
using Matrix = Microsoft.Xna.Framework.Matrix;
using Quaternion = BEPUutilities.Quaternion;
using Vector3 = Microsoft.Xna.Framework.Vector3;
using Vector4 = Microsoft.Xna.Framework.Vector4;

namespace Engine.Logic
{
    public class MainSceneLogic
    {
        #region FIELDS

        ////////////////////////////////////////////////////////////////////////////////////////////////////////////
        //  VARIABLES
        ////////////////////////////////////////////////////////////////////////////////////////////////////////////

        private Assets _assets;

        // SceneManager owns the active scene; everything content-related is reached
        // through ActiveScene. MainSceneLogic keeps the runtime drivers (physics,
        // mesh library, SDF, editor camera, debug entities).
        public readonly SceneManager SceneManager = new SceneManager(new Scene());
        public Scene ActiveScene => SceneManager.ActiveScene;

        // Editor-only camera. Active in Edit mode; Play mode swaps in the scene's
        // MainCamera (the actual gameplay camera).
        public EditorCamera EditorCamera;

        // Owns Play/Stop transitions. Constructed in Initialize() so its constructor
        // can capture a reference back to this MainSceneLogic.
        public PlayModeController PlayMode;

        // Active rendering camera. The renderer reads this; setters (SetUpEditorScene)
        // write through to the scene's MainCamera so saved files capture the game
        // camera. In Play mode the scene's MainCamera takes over.
        public Camera Camera
        {
            get
            {
                if (PlayMode != null && PlayMode.Mode == GameMode.Play && ActiveScene.MainCamera != null)
                    return ActiveScene.MainCamera;
                return (Camera)EditorCamera ?? ActiveScene.MainCamera;
            }
            set => ActiveScene.MainCamera = value;
        }


        //mesh library, holds all the meshes and their materials
        public MeshMaterialLibrary MeshMaterialLibrary;

        // Forward to the active scene so existing callers (ScreenManager, Renderer,
        // EditorLogic) keep working unchanged. Lists themselves live on Scene.
        public List<BasicEntity> BasicEntities => ActiveScene.BasicEntities;
        public List<Decal> Decals => ActiveScene.Decals;
        public List<PointLight> PointLights => ActiveScene.PointLights;
        public List<DirectionalLight> DirectionalLights => ActiveScene.DirectionalLights;

        public readonly List<DebugEntity> DebugEntities = new List<DebugEntity>();

        public EnvironmentSample EnvironmentSample
        {
            get => ActiveScene.EnvironmentSample;
            set => ActiveScene.EnvironmentSample = value;
        }

        //Which render target are we currently displaying?
        private int _renderModeCycle;
        private Space _physicsSpace;

        //SDF
        public SdfGenerator _sdfGenerator;

        #endregion

        ////////////////////////////////////////////////////////////////////////////////////////////////////////////
        //  FUNCTIONS
        ////////////////////////////////////////////////////////////////////////////////////////////////////////////

            ////////////////////////////////////////////////////////////////////////////////////////////////////////////
            //  MAIN FUNCTIONS
            ////////////////////////////////////////////////////////////////////////////////////////////////////////////

        //Done after Load
        public void Initialize(Assets assets, Space space, GraphicsDevice graphicsDevice)
        {
            _assets = assets;
            _physicsSpace = space;

            MeshMaterialLibrary = new MeshMaterialLibrary(graphicsDevice);

            SceneManager.Assets = assets;
            SceneManager.SceneChanged += OnSceneChanged;

            PlayMode = new PlayModeController(this);

            SetUpEmptyEditorScene(graphicsDevice);
        }

        /// <summary>
        /// Runtime swap when SceneManager flips ActiveScene. Detaches the old scene's
        /// physics bodies, clears the mesh/material library, and re-registers the new
        /// scene's content. Selection in EditorLogic must be cleared by callers
        /// (the bridge handles this through its own selection sync).
        /// </summary>
        private void OnSceneChanged(Scene oldScene, Scene newScene)
        {
            EditorBridge.Log($"MainSceneLogic.OnSceneChanged: '{oldScene?.Name}' -> '{newScene?.Name}'");

            // Detach old physics bodies (only the dynamic + static ones BEPU knows about).
            if (oldScene != null && _physicsSpace != null)
            {
                for (int i = 0; i < oldScene.BasicEntities.Count; i++)
                {
                    BasicEntity e = oldScene.BasicEntities[i];
                    try
                    {
                        if (e.StaticPhysicsObject != null) _physicsSpace.Remove(e.StaticPhysicsObject);
                    }
                    catch (Exception ex) { EditorBridge.Log("physics detach static threw: " + ex); }
                }
            }

            // Reset the mesh/material library — new scene re-registers its entities below.
            MeshMaterialLibrary?.Clear();

            // A New Scene comes through as an empty Scene{} with MainCamera and
            // EnvironmentSample = null. The renderer's environment probe + game
            // camera fall-back both NRE on null, which silently breaks the
            // render loop ("game stops moving" — every Draw throws and the
            // back-buffer is never updated). Loaded scenes don't hit this
            // because save/load restores MainCamera and EnvironmentSample.
            // Populate runtime defaults here so a fresh scene is renderable.
            if (newScene != null)
            {
                if (newScene.MainCamera == null)
                    newScene.MainCamera = new Camera(position: new Vector3(-88, -11f, 4), lookat: new Vector3(38, 8, 32));
                if (newScene.EnvironmentSample == null)
                    newScene.EnvironmentSample = new EnvironmentSample(new Vector3(-45, -5, 5));
            }

            // Re-register the new scene's BasicEntities into the mesh library so the
            // renderer can draw them. Their TransformMatrix carries the IDs already.
            if (newScene != null && MeshMaterialLibrary != null)
            {
                for (int i = 0; i < newScene.BasicEntities.Count; i++)
                {
                    BasicEntity e = newScene.BasicEntities[i];
                    e.RegisterInLibrary(MeshMaterialLibrary);
                }
            }

            // Anything left in Play state from the previous scene must be reverted —
            // a fresh scene has no scripts, no MainCamera that's safe to drive
            // gameplay, and physics gravity should not still be acting on the
            // departed entities. Reset to Edit mode unconditionally.
            try { PlayMode?.Stop(); }
            catch (Exception ex) { EditorBridge.Log("PlayMode.Stop on scene change threw: " + ex); }
        }

        // Boots an empty scene: just a camera, environment sample, and one sun-like
        // directional light. Everything else is added by the user via the editor's
        // Assets panel + Hierarchy. (Previously this method instantiated the Sponza
        // demo, a plane grid, the Stanford dragon, physics spheres, decals, and three
        // point lights — now removed; the demo content lives only as a sample scene
        // file in the future.)
        private void SetUpEmptyEditorScene(GraphicsDevice graphics)
        {
            // Scene's game camera (saved with the scene).
            ActiveScene.MainCamera = new Camera(position: new Vector3(-88, -11f, 4), lookat: new Vector3(38, 8, 32));

            // Editor camera — separate, never serialised. Phase 6 picks between the
            // editor camera and the scene's main camera based on Play/Edit mode.
            EditorCamera = new EditorCamera(position: new Vector3(-88, -11f, 4), lookat: new Vector3(38, 8, 32));

            EnvironmentSample = new EnvironmentSample(new Vector3(-45, -5, 5));

            _sdfGenerator = new SdfGenerator();

            // One sun so an imported model is visible in the empty viewport.
            AddDirectionalLight(direction: new Vector3(0.2f, 0.2f, -1),
                intensity: 100,
                color: Color.White,
                position: Vector3.UnitZ * 2,
                drawShadows: true,
                shadowWorldSize: 450,
                shadowDepth: 180,
                shadowResolution: 1024,
                shadowFilteringFiltering: DirectionalLight.ShadowFilteringTypes.SoftPCF3x,
                screenspaceShadowBlur: false);
        }



        /// <summary>
        /// Main logic update function. Is called once per frame. Use this for all program logic and user inputs
        /// </summary>
        /// <param name="gameTime">Can use this to compute the delta between frames</param>
        /// <param name="isActive">The window status. If this is not the active window we shouldn't do anything</param>
        public void Update(GameTime gameTime, bool isActive)
        {
            if (!isActive) return;

            //Upd
            Input.Update(gameTime, Camera);

            // Scripts tick only in Play mode.
            PlayMode?.UpdateScripts(gameTime);

            //VolumeTexture.RotationMatrix = testEntity.WorldTransform.InverseWorld;
            //VolumeTexture.Scale = testEntity.WorldTransform.Scale;
            
            //Make the lights move up and down
            //for (var i = 2; i < PointLights.Count; i++)
            //{
            //    PointLight point = PointLights[i];
            //    point.Position = new Vector3(point.Position.X, point.Position.Y, (float)(Math.Sin(gameTime.TotalGameTime.TotalSeconds * 0.8f + i) * 10 - 13));
            //}

            //KeyInputs for specific tasks


            //If we are currently typing stuff into the console we should ignore the following keyboard inputs
            if (DebugScreen.ConsoleOpen) return;

            //Starts the "editor mode" where we can manipulate objects
            if (Input.WasKeyPressed(Keys.Space))
            {
                GameSettings.e_enableeditor = !GameSettings.e_enableeditor;
            }

            
            
            //Spawns a new light on the ground
            if (Input.keyboardState.IsKeyDown(Keys.L))
            {
                AddPointLight(position: new Vector3(FastRand.NextSingle() * 250 - 125, FastRand.NextSingle() * 50 - 25, FastRand.NextSingle() * 30 - 19), 
                    radius: 20, 
                    color: FastRand.NextColor(), 
                    intensity: 40, 
                    castShadows: false,
                    isVolumetric: true);
            }
            
            //Switch which rendertargets we show
            if (Input.WasKeyPressed(Keys.F1))
            {
                _renderModeCycle++;
                if (_renderModeCycle > Enum.GetNames(typeof(Renderer.Renderer.RenderModes)).Length - 1) _renderModeCycle = 0;

                GameSettings.g_rendermode = (Renderer.Renderer.RenderModes) _renderModeCycle;
            }
        }
        

        //Load content
        public void Load(ContentManager content)
        {
            //...
        }
        
        ////////////////////////////////////////////////////////////////////////////////////////////////////////////
        //  HELPER FUNCTIONS
        ////////////////////////////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// Spawn a directional light (omni light). This light covers everything and comes from a single point from infinte distance. 
        /// Good for something like a sun
        /// </summary>
        /// <param name="direction">The direction the light is facing in world space coordinates</param>
        /// <param name="intensity"></param>
        /// <param name="color"></param>
        /// <param name="position">The position is only relevant if drawing shadows</param>
        /// <param name="drawShadows"></param>
        /// <param name="shadowWorldSize">WorldSize is the width/height of the view projection for shadow mapping</param>
        /// <param name="shadowDepth">FarClip for shadow mapping</param>
        /// <param name="shadowResolution"></param>
        /// <param name="shadowFilteringFiltering"></param>
        /// <param name="screenspaceShadowBlur"></param>
        /// <param name="staticshadows">These shadows will not be updated once they are created, moving objects will be shadowed incorrectly</param>
        /// <returns></returns>
        private DirectionalLight AddDirectionalLight(Vector3 direction, int intensity, Color color, Vector3 position = default(Vector3), bool drawShadows = false, float shadowWorldSize = 100, float shadowDepth = 100, int shadowResolution = 512, DirectionalLight.ShadowFilteringTypes shadowFilteringFiltering = DirectionalLight.ShadowFilteringTypes.Poisson, bool screenspaceShadowBlur = false, bool staticshadows = false )
        {
            DirectionalLight light = new DirectionalLight(color: color, 
                intensity: intensity, 
                direction: direction, 
                position: position, 
                castShadows: drawShadows, 
                shadowSize: shadowWorldSize, 
                shadowDepth: shadowDepth, 
                shadowResolution: shadowResolution, 
                shadowFiltering: shadowFilteringFiltering, 
                screenspaceshadowblur: screenspaceShadowBlur, 
                staticshadows: staticshadows);
            DirectionalLights.Add(light);
            return light;
        }

        //The function to use for new pointlights
        /// <summary>
        /// Add a point light to the list of drawn point lights
        /// </summary>
        /// <param name="position"></param>
        /// <param name="radius"></param>
        /// <param name="color"></param>
        /// <param name="intensity"></param>
        /// <param name="castShadows">will render shadow maps</param>
        /// <param name="isVolumetric">does it have a fog volume?</param>
        /// <param name="volumetricDensity">How dense is the volume?</param>
        /// <param name="shadowResolution">shadow map resolution per face. Optional</param>
        /// <param name="staticShadow">if set to true the shadows will not update at all. Dynamic shadows in contrast update only when needed.</param>
        /// <returns></returns>
        private PointLight AddPointLight(Vector3 position, float radius, Color color, float intensity, bool castShadows, bool isVolumetric = false, float volumetricDensity = 1, int shadowResolution = 256, int softShadowBlurAmount = 0, bool staticShadow = false)
        {
            PointLight light = new PointLight(position, radius, color, intensity, castShadows, isVolumetric, shadowResolution, softShadowBlurAmount, staticShadow, volumetricDensity);
            PointLights.Add(light);
            return light;
        }


        /// <summary>
        /// Create a basic rendered model without custom material, use materialEffect: for materials instead
        /// The material used is the one found in the imported model file, usually that means diffuse texture only
        /// </summary>
        /// <param name="model"></param>
        /// <param name="position"></param>
        /// <param name="angleX"></param>
        /// <param name="angleY"></param>
        /// <param name="angleZ"></param>
        /// <param name="scale"></param>
        /// <param name="PhysicsEntity">attached physical object</param>
        /// <param name="hasStaticPhysics">if "true" a static mesh will be computed based on the model mesh. Other physical objects can collide with the entity</param>
        /// <returns>returns the basicEntity we created</returns>
        private BasicEntity AddEntity(ModelDefinition model, Vector3 position, double angleX, double angleY, double angleZ, float scale, Entity PhysicsEntity = null, bool hasStaticPhysics = false)
        {
            BasicEntity entity = new BasicEntity(model,
                null, 
                position: position, 
                angleZ: angleZ, 
                angleX: angleX, 
                angleY: angleY, 
                scale: Vector3.One * scale,
                library: MeshMaterialLibrary,
                physicsObject: PhysicsEntity);
            BasicEntities.Add(entity);

            if (hasStaticPhysics) AddStaticPhysics(entity);

            return entity;
        }

        /// <summary>
        /// Create a basic rendered model with custom material
        /// </summary>
        /// <param name="model"></param>
        /// <param name="materialEffect">custom material</param>
        /// <param name="position"></param>
        /// <param name="angleX"></param>
        /// <param name="angleY"></param>
        /// <param name="angleZ"></param>
        /// <param name="scale"></param>
        /// <param name="PhysicsEntity">attached physical object</param>
        /// <param name="hasStaticPhysics">if "true" a static mesh will be computed based on the model mesh. Other physical objects can collide with the entity</param>
        /// <returns>returns the basicEntity we created</returns>
        private BasicEntity AddEntity(ModelDefinition model, MaterialEffect materialEffect, Vector3 position, double angleX, double angleY, double angleZ, float scale, Entity PhysicsEntity = null, bool hasStaticPhysics = false )
        {
            BasicEntity entity = new BasicEntity(model,
                materialEffect,
                position: position,
                angleZ: angleZ,
                angleX: angleX,
                angleY: angleY,
                scale: Vector3.One * scale,
                library: MeshMaterialLibrary,
                physicsObject: PhysicsEntity);
            BasicEntities.Add(entity);

            if(hasStaticPhysics) AddStaticPhysics(entity);

            return entity;
        }

        /// <summary>
        /// Create a static physics mesh from a model and scale.
        /// </summary>
        /// <param name="entity"></param>
        private void AddStaticPhysics(BasicEntity entity)
        {
            BEPUutilities.Vector3[] vertices;
            int[] indices;
            ModelDataExtractor.GetVerticesAndIndicesFromModel(entity.Model, out vertices, out indices);
            var mesh = new StaticMesh(vertices, indices,
                new AffineTransform(
                    new BEPUutilities.Vector3(entity.Scale.X, entity.Scale.Y, entity.Scale.Z),
                Quaternion.CreateFromRotationMatrix(MathConverter.Convert(entity.RotationMatrix)),
                MathConverter.Convert(entity.Position)));

            entity.StaticPhysicsObject = mesh;
            _physicsSpace.Add(mesh);
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////////////
        //  EDITOR BRIDGE HELPERS — invoked from EditorBridge on the game thread
        ////////////////////////////////////////////////////////////////////////////////////////////////////////////

        internal PointLight EditorAddPointLight(Vector3 position, float radius, Color color, float intensity)
        {
            return AddPointLight(position, radius, color, intensity, castShadows: false);
        }

        internal DirectionalLight EditorAddDirectionalLight(Vector3 direction, Color color, float intensity)
        {
            return AddDirectionalLight(direction: direction, intensity: (int)intensity, color: color);
        }

        internal BasicEntity EditorAddBasicEntity(ModelDefinition model, MaterialEffect material, Vector3 position)
        {
            if (model == null) return null;
            if (material != null)
                return AddEntity(model, material, position, 0, 0, 0, 1f);
            return AddEntity(model, position, 0, 0, 0, 1f);
        }

        internal bool EditorDelete(int id)
        {
            for (int i = 0; i < BasicEntities.Count; i++)
            {
                if (BasicEntities[i].Id != id) continue;
                BasicEntity entity = BasicEntities[i];
                MeshMaterialLibrary?.DeleteFromRegistry(entity);
                BasicEntities.RemoveAt(i);
                return true;
            }
            for (int i = 0; i < PointLights.Count; i++)
            {
                if (PointLights[i].Id != id) continue;
                PointLights.RemoveAt(i);
                return true;
            }
            for (int i = 0; i < DirectionalLights.Count; i++)
            {
                if (DirectionalLights[i].Id != id) continue;
                DirectionalLights.RemoveAt(i);
                return true;
            }
            for (int i = 0; i < Decals.Count; i++)
            {
                if (Decals[i].Id != id) continue;
                Decals.RemoveAt(i);
                return true;
            }
            return false;
        }

    }
}
