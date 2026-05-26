using System;
using System.Globalization;
using System.IO;
using BEPUphysics;
using Engine.Editor;
using Engine.Recources;
using HelperSuite.GUIRenderer;
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
        private GUIRenderer _guiRenderer;
        private MainSceneLogic _sceneLogic;
        private GUILogic _guiLogic;
        private EditorLogic _editorLogic;
        private Assets _assets;
        private ShaderManager _shaderManager;
        private DebugScreen _debug;
        private VideoIntroLogic _videoIntro;
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

        public void Initialize(GraphicsDevice graphicsDevice, Space space)
        {
            _graphicsDevice = graphicsDevice;
            _spriteBatch = new SpriteBatch(graphicsDevice);
            _videoIntro.Initialize();
            _renderer.Initialize(graphicsDevice, _assets);
            _sceneLogic.Initialize(_assets, space, graphicsDevice);
            _guiLogic.Initialize(_assets, _sceneLogic.Camera);
            _editorLogic.Initialize(graphicsDevice);
            _debug.Initialize(graphicsDevice);
            _guiRenderer.Initialize(graphicsDevice, GameSettings.g_screenwidth, GameSettings.g_screenheight);

            _bridge?.Bind(_sceneLogic, _editorLogic, _assets);

            // Under Anvil, the in-engine "Editor Mode" toggle in the legacy GUI
            // is hidden — so the selection/outline/gizmo render pass would
            // never get turned on and viewport clicks would feel dead even
            // though e_enableeditor is true. Force-enable the selection pass
            // when hosted; standalone keeps the default-off behaviour (Space
            // still works, the HelperSuite toggle still works).
            if (_bridge?.IsHostedByEditor == true)
                GameStats.e_EnableSelection = true;
        }

        //Update per frame
        public void Update(GameTime gameTime, bool isActive)
        {
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
            // Skip the legacy in-engine GUI under Anvil — Anvil owns the editor UI.
            // Leaving it on doubles up the inspector + duplicates mouse-input gating
            // (GUIControl.UIWasUsed) which interferes with viewport picking.
            bool hosted = _bridge?.IsHostedByEditor ?? false;
            if (!hosted)
                _guiLogic.Update(gameTime, isActive, _editorLogic.SelectedObject);
            _editorLogic.Update(gameTime, _sceneLogic.BasicEntities, _sceneLogic.Decals, _sceneLogic.PointLights, _sceneLogic.DirectionalLights, _sceneLogic.EnvironmentSample, _sceneLogic.DebugEntities, _editorReceivedDataBuffer, _sceneLogic.MeshMaterialLibrary);
            _sceneLogic.Update(gameTime, isActive);
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
            _guiLogic = new GUILogic();
            _editorLogic = new EditorLogic();
            _assets = new Assets();
            _debug = new DebugScreen();
            _guiRenderer = new GUIRenderer();
            _videoIntro = new VideoIntroLogic();

            Globals.content = content;
            Shaders.Load(content);
            _shaderManager = new ShaderManager(content, graphicsDevice);
            _assets.Load(content, graphicsDevice);
            _renderer.Load(content, _shaderManager);
            _sceneLogic.Load(content);
            _debug.LoadContent(content);
            _guiRenderer.Load(content);
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
            
            // Legacy HelperSuite GUI overlay — drawn only in standalone Engine.exe.
            // Anvil replaces it; see Update() above.
            if (GameSettings.e_enableeditor && GameSettings.ui_enabled
                && !(_bridge?.IsHostedByEditor ?? false))
                _guiRenderer.Draw(_guiLogic.GuiCanvas);

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
            _guiLogic.UpdateResolution();
        }

        public void Dispose()
        {
            _spriteBatch?.Dispose();
            _guiRenderer?.Dispose();
        }
    }
}
