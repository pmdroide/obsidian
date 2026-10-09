using System;
using Engine.Editor;
using Engine.Physics;
using Engine.Recources;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Vista;

namespace Engine.Logic
{
    /// <summary>
    /// Manages our different screens and passes information accordingly
    /// </summary>
    public class ScreenManager : IDisposable
    {
        public enum GameState
        {
            VideoIntro,
            MainGame
        }

        private GameState _currentState = GameState.VideoIntro;
        ////////////////////////////////////////////////////////////////////////////////////////////////////////////
        //  VARIABLES
        ////////////////////////////////////////////////////////////////////////////////////////////////////////////

        private Renderer.Renderer _renderer;
        private MainSceneLogic _sceneLogic;
        private EditorLogic _editorLogic;
        private Assets _assets;
        private ShaderManager _shaderManager;
        private DebugScreen _debug;
        private VideoIntroLogic _videoIntro;
        private AudioManager _audio;
        private SpriteBatch _spriteBatch;
        private GraphicsDevice _graphicsDevice;

        // Fonts for script UI (GameUI).
        private readonly UIFontRegistry _uiFonts = new UIFontRegistry();

        private EditorLogic.EditorReceivedData _editorReceivedDataBuffer;

        private readonly EditorBridge _bridge;

        ////////////////////////////////////////////////////////////////////////////////////////////////////////////
        //  FUNCTIONS
        ////////////////////////////////////////////////////////////////////////////////////////////////////////////

        public ScreenManager() { }

        public ScreenManager(EditorBridge bridge)
        {
            _bridge = bridge;
        }

        public void Initialize(GraphicsDevice graphicsDevice, PhysicsSystem physics)
        {
            _graphicsDevice = graphicsDevice;
            _spriteBatch = new SpriteBatch(graphicsDevice);

            // The Anvil editor is an exception: it skips the standalone intro video
            // entirely (never loaded — see Load) and boots straight into the live
            // scene. Standalone Engine.exe plays the intro as usual.
            if (_bridge?.IsHostedByEditor == true)
                _currentState = GameState.MainGame;
            else
                _videoIntro.Initialize();

            _renderer.Initialize(graphicsDevice, _assets);
            _audio.Initialize("Content");
            if (_bridge?.IsHostedByEditor == true)
                _audio.EditorContentRoot = AssetImporter.LocateEngineContentRoot();
            _sceneLogic.Initialize(_assets, physics, graphicsDevice);
            _editorLogic.Initialize(graphicsDevice);
            _debug.Initialize(graphicsDevice);

            _bridge?.Bind(_sceneLogic, _editorLogic, _assets);

            // Build order of the game's scenes; entry 0 starts the standalone game (see Update).
            SceneList.Load();
            GameUI.Bind(graphicsDevice, _uiFonts);

            // Under Anvil, the in-engine "Editor Mode" toggle (formerly in
            // HelperSuite's right-side panel) no longer exists, so force the
            // selection/outline/gizmo render pass on when hosted. Standalone
            // keeps the default-off behaviour (Space still toggles).
            if (_bridge?.IsHostedByEditor == true)
                GameStats.e_EnableSelection = true;
        }

        //Update per frame
        public void Update(GameTime gameTime, bool isActive)
        {
            // Pump FMOD every frame, regardless of game state (intro included).
            _audio?.SystemUpdate();

            if (_currentState == GameState.VideoIntro)
            {
                _videoIntro.Update(isActive);
                if (_videoIntro.HasFinished)
                {
                    _currentState = GameState.MainGame;
                    // The standalone game plays scene 0 of the scene list; Anvil stays in Edit.
                    _sceneLogic.StartFirstScene();
                }
                return;
            }

#if DEBUG
            _shaderManager.CheckForChanges();
#endif
            _editorLogic.Update(gameTime, _sceneLogic.BasicEntities, _sceneLogic.Decals, _sceneLogic.PointLights, _sceneLogic.DirectionalLights, _sceneLogic.EnvironmentSample, _sceneLogic.DebugEntities, _editorReceivedDataBuffer, _sceneLogic.MeshMaterialLibrary);
            _sceneLogic.Update(gameTime, isActive);
            // Listener follows the active camera; 3D emitters reconcile after scene positions advance.
            _audio?.UpdateListener(_sceneLogic.Camera);
            _renderer.Update(gameTime, isActive, _sceneLogic._sdfGenerator, _sceneLogic.BasicEntities);

            //Hands finished bakes to their scene and keeps the probe volume textures in sync
            _sceneLogic.Lighting.Update(_graphicsDevice, _sceneLogic.ActiveScene);

            _debug.Update(gameTime);

            // Script UI (menus, HUDs) after the scripts changed it this frame.
            GameUI.Update(gameTime);

            // Drain queued editor ops + publish snapshot. Runs on the game thread,
            // strictly after all logic mutations but before the next Draw.
            _bridge?.DrainAndPublish();
        }

        /// <summary>Sync + step scene physics (BEPU v2). Called by Engine after <see cref="Update"/>.</summary>
        /// <param name="time">Total game time in seconds: the clock the water shader animates with.</param>
        public void UpdatePhysics(float dt, float time = 0)
        {
            if (_currentState == GameState.VideoIntro) return;
            _sceneLogic.UpdatePhysics(dt, time);
        }

        //Load content
        public void Load(ContentManager content, GraphicsDevice graphicsDevice)
        {
            _renderer = new Renderer.Renderer();
            _sceneLogic = new MainSceneLogic();
            _editorLogic = new EditorLogic();
            _assets = new Assets();
            _debug = new DebugScreen();
            _videoIntro = new VideoIntroLogic();

            Globals.content = content;
            Shaders.Load(content);
            _shaderManager = new ShaderManager(content, graphicsDevice);
            _assets.Load(content, graphicsDevice);
            _audio = new AudioManager();
            _renderer.Load(content, _shaderManager);
            _sceneLogic.Load(content);
            _debug.LoadContent(content);

            // Don't spin up LibVLC / decode the intro mp4 when hosted by the editor;
            // the intro is a standalone-only screen (see Initialize). VideoIntroLogic
            // is null-safe, so leaving it unloaded keeps Update/Draw/Unload no-ops.
            if (_bridge?.IsHostedByEditor != true)
                _videoIntro.Load(content, graphicsDevice);

            LoadUIFonts(content);
        }

        // Fonts the GameUI documents' CSS can reference by font-family.
        private void LoadUIFonts(ContentManager content)
        {
            var defaultFont = content.Load<SpriteFont>("Fonts/defaultFont");
            var monospaceFont = content.Load<SpriteFont>("Fonts/monospace");
            _uiFonts.Register("default", defaultFont, isDefault: true);
            _uiFonts.Register("monospace", monospaceFont);
            // Game UI faces, sized for a 1080-high design canvas (GameUI scales layers to the window).
            _uiFonts.Register("display", content.Load<SpriteFont>("Fonts/UI/Display"));
            _uiFonts.Register("heading", content.Load<SpriteFont>("Fonts/UI/Heading"));
            _uiFonts.Register("body", content.Load<SpriteFont>("Fonts/UI/Body"));
            _uiFonts.Register("caption", content.Load<SpriteFont>("Fonts/UI/Caption"));
        }

        public void Unload(ContentManager content)
        {
            _videoIntro.Unload();
            _audio?.Dispose();
            _renderer?.UnloadEnvironment();
            _sceneLogic?.Lighting.Dispose();
            if (_sceneLogic?.MeshMaterialLibrary != null)
                foreach (var entity in _sceneLogic.BasicEntities) entity.Dispose(_sceneLogic.MeshMaterialLibrary);
            content.Dispose();
        }

        public void Draw(GameTime gameTime)
        {
            if (_currentState == GameState.VideoIntro)
            {
                _graphicsDevice.Clear(Color.Black); // Clear the screen first

                _spriteBatch.Begin(); // Start the batch here
                _videoIntro.Draw(_spriteBatch);
                _spriteBatch.End(); // End it here
                return;
            }

            //Our renderer gives us information on what id is currently hovered over so we can update / manipulate objects in the logic functions
            _editorReceivedDataBuffer = _renderer.Draw(_sceneLogic.Camera,
                _sceneLogic.MeshMaterialLibrary,
                _sceneLogic.BasicEntities, _sceneLogic.Decals,
                pointLights: _sceneLogic.PointLights,
                directionalLights: _sceneLogic.DirectionalLights,
                envSample: _sceneLogic.EnvironmentSample,
                debugEntities: _sceneLogic.DebugEntities,
                editorData: _editorLogic.GetEditorData(),
                gameTime: gameTime,
                lighting: _sceneLogic.Lighting,
                lightingSettings: _sceneLogic.ActiveScene.Lighting,
                scene: _sceneLogic.ActiveScene);

            // Script UI (menus, HUDs) over the scene, under the debug stats and console.
            GameUI.Draw(_spriteBatch);

            // Engine debug stats (GameSettings.u_showdisplayinfo; Anvil's viewport Stats toggle) on top.
            _debug.Draw(gameTime);
        }

        public void UpdateResolution()
        {
            _renderer.UpdateResolution();
        }

        public void Dispose()
        {
            _spriteBatch?.Dispose();
        }
    }
}
