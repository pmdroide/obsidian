using System;
using System.Globalization;
using System.IO;
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

        private UIManager _vistaUI;
        private double _vistaSmoothFps = 60;
        private double _vistaFpsRefresh;
        private long _vistaMaxGcMemory;

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
            _sceneLogic.Initialize(_assets, physics, graphicsDevice);
            _editorLogic.Initialize(graphicsDevice);
            _debug.Initialize(graphicsDevice);

            _bridge?.Bind(_sceneLogic, _editorLogic, _assets);

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
                _videoIntro.Update();
                if (_videoIntro.HasFinished)
                {
                    _currentState = GameState.MainGame;
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

            _debug.Update(gameTime);

            UpdateVistaUI(gameTime);

            // Drain queued editor ops + publish snapshot. Runs on the game thread,
            // strictly after all logic mutations but before the next Draw.
            _bridge?.DrainAndPublish();
        }

        // Update the Vista UI with performance metrics and other dynamic information.
        private void UpdateVistaUI(GameTime gameTime)
        {
            if (_vistaUI == null || !GameSettings.ui_vista_enabled) return;

            double frameMs = gameTime.ElapsedGameTime.TotalMilliseconds;
            if (frameMs > 0.0)
            {
                double instantaneous = 1000.0 / frameMs;
                _vistaSmoothFps = 0.95 * _vistaSmoothFps + 0.05 * instantaneous;
            }

            // Throttle text updates to twice/sec — AngleSharp DOM writes aren't free.
            double nowMs = gameTime.TotalGameTime.TotalMilliseconds;
            if (nowMs - _vistaFpsRefresh < 500.0) { _vistaUI.Update(gameTime); return; }
            _vistaFpsRefresh = nowMs;

            long mem = GC.GetTotalMemory(false);
            if (mem > _vistaMaxGcMemory) _vistaMaxGcMemory = mem;

            CultureInfo inv = CultureInfo.InvariantCulture;
            _vistaUI.SetText("#perf-fps",   "FPS: "   + Math.Round(_vistaSmoothFps).ToString(inv));
            _vistaUI.SetText("#perf-frame", "Frame: " + frameMs.ToString("0.00", inv) + " ms");
            _vistaUI.SetText("#perf-res",   "Res: "   + GameSettings.g_screenwidth + " x " + GameSettings.g_screenheight);
            _vistaUI.SetText("#perf-mem",   "Mem: "   + (mem / 1024).ToString(inv) + " / " + (_vistaMaxGcMemory / 1024).ToString(inv) + " KB");

            _vistaUI.Update(gameTime);
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

            LoadVistaUI(content, graphicsDevice);
        }

        // Load Vista UI helper functions
        private void LoadVistaUI(ContentManager content, GraphicsDevice graphicsDevice)
        {
            _vistaUI = new UIManager(graphicsDevice);

            // Register fonts the CSS can reference by font-family.
            var defaultFont = content.Load<SpriteFont>("Fonts/defaultFont");
            var monospaceFont = content.Load<SpriteFont>("Fonts/monospace");
            _vistaUI.Fonts.Register("default", defaultFont, isDefault: true);
            _vistaUI.Fonts.Register("monospace", monospaceFont);

            string baseDir = AppContext.BaseDirectory;
            string xmlPath = Path.Combine(baseDir, "Content", "UI", "debug.xml");
            string cssPath = Path.Combine(baseDir, "Content", "UI", "debug.css");

            if (File.Exists(xmlPath) && File.Exists(cssPath))
            {
                _vistaUI.LoadUI(xmlPath, cssPath);
            }
        }

        public void Unload(ContentManager content)
        {
            _videoIntro.Unload();
            _audio?.Dispose();
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
                gameTime: gameTime);

            _debug.Draw(gameTime);

            // Vista UI on top of everything
            if (_vistaUI != null && GameSettings.ui_vista_enabled)
            {
                _spriteBatch.Begin();
                _vistaUI.Draw(_spriteBatch);
                _spriteBatch.End();
            }
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
