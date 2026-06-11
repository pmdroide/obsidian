using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
//using Microsoft.Xna.Framework.Input;
using Keys = Microsoft.Xna.Framework.Input.Keys;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using Engine.Editor;
using Engine.Logic;
using Engine.Physics;
using Engine.Recources;

namespace Engine
{
    /// <summary>
    /// This is the main type for your game.
    /// </summary>
    public class Engine : Game
    {
        //VARIABLES
        //GraphicsDeviceManager graphics;
        private readonly GraphicsDeviceManager _graphics;

        private readonly ScreenManager _screenManager;

        private readonly EditorBridge _bridge;

        public IEditorBridge Bridge => _bridge;

        private readonly PhysicsSystem _physics;

        //Do not change, these are overwritten (Check GameSettings.cs in Resources
        private bool _vsync = true;
        private int _fixFPS = 0;
        private bool _isActive = true;

        //Set by the WinForms resize event; the real swap-chain/render-target rebuild is
        //deferred to the next Update so it runs OUTSIDE the WndProc/OnResize callstack.
        private bool _pendingResize;
        //Last client size observed by ApplyPendingResize, used to debounce a live drag so the
        //(expensive) render-target rebuild only happens once the size settles.
        private int _lastResizeW;
        private int _lastResizeH;
        //When hosted in Anvil the engine HWND is reparented/resized to the container without ever
        //raising a ClientSizeChanged we observe, so the very first frames render at MonoGame's stale
        //default backbuffer (640x480) until the user manually resizes. This makes the first few
        //Update ticks self-prime a reconcile so the viewport fits the container on boot.
        private int _bootReconcileTicks = 120;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        //Authoritative client size of the engine HWND. When Anvil reparents/resizes the window
        //from the outside (MoveWindow/SetWindowPos), MonoGame's cached Window.ClientBounds can stay
        //at the stale 640x480 default — but the real Win32 client rect is always current. Prefer it,
        //falling back to ClientBounds (standalone, or if the handle isn't ready yet).
        private void GetRealClientSize(out int w, out int h)
        {
            IntPtr hwnd = Window?.Handle ?? IntPtr.Zero;
            if (hwnd != IntPtr.Zero && GetClientRect(hwnd, out var r))
            {
                w = r.Right - r.Left;
                h = r.Bottom - r.Top;
                if (w > 0 && h > 0) return;
            }
            w = Window.ClientBounds.Width;
            h = Window.ClientBounds.Height;
        }

        public Engine()
        {
            //Initialize graphics and content
            _graphics = new GraphicsDeviceManager(this);
            Content.RootDirectory = "Content";

            //Bridge between the engine and an external editor (Anvil). The engine
            //works fine without an attached editor — the bridge just sits idle.
            _bridge = new EditorBridge();

            //Initialize screen manager, which controls draw / logic for our screens
            _screenManager = new ScreenManager(_bridge);

            //Initialize our physics (BEPUphysics v2) and give it gravity. Z is up in
            //this engine, so gravity points down the negative Z axis.
            _physics = new PhysicsSystem(new Vector3(0, 0, -9.81f));

            //Size of our application / starting back buffer
            _graphics.PreferredBackBufferWidth = GameSettings.g_screenwidth;
            _graphics.PreferredBackBufferHeight = GameSettings.g_screenheight;

            //HiDef enables usable shaders
            _graphics.GraphicsProfile = GraphicsProfile.HiDef;

            //_graphics.GraphicsDevice.DeviceLost += new EventHandler<EventArgs>(ClientLostDevice);

            //Mouse should not disappear
            IsMouseVisible = true;

            //Window settings
            Window.AllowUserResizing = true;
            Window.IsBorderless = false;

            //Update all our rendertargets when we resize
            Window.ClientSizeChanged += ClientChangedWindowSize;

            //Update framerate etc. when not the active window
            Activated += IsActivated;
            Deactivated += IsDeactivated;
        }

        /// <summary>
        /// Allows the game to perform any initialization it needs to before starting to run.
        /// This is where it can query for any required services and load any non-graphic
        /// related content.  Calling base.Initialize will enumerate through any components
        /// and initialize them as well.
        /// </summary>
        protected override void Initialize()
        {
            this.Window.Title = "Engine";

            _screenManager.Load(Content, GraphicsDevice);
            // TODO: Add your initialization logic here
            _screenManager.Initialize(GraphicsDevice, _physics);

            base.Initialize();
        }

        /// <summary>
        /// LoadContent will be called once per game and is the place to load
        /// all of your content.
        /// </summary>
        protected override void LoadContent()
        {
            // TODO: use this.Content to load your game content here
        }

        /// <summary>
        /// UnloadContent will be called once per game and is the place to unload
        /// game-specific content.
        /// </summary>
        protected override void UnloadContent()
        {
            // TODO: Unload any non ContentManager content here
            _screenManager.Unload(Content);
            _physics?.Dispose();
        }

        /// <summary>
        /// Allows the game to run logic such as updating the world,
        /// checking for collisions, gathering input, and playing audio.
        /// </summary>
        /// <param name="gameTime">Provides a snapshot of timing values.</param>
        protected override void Update(GameTime gameTime)
        {
            //Apply any pending window resize here, on the game thread but OUTSIDE the
            //WinForms WndProc/OnResize callstack. Resetting the swap chain from inside
            //that callstack NREs on a reparented child window (the Anvil viewport). Done
            //before the _isActive gate so a resize still lands while the embedded engine
            //window is not the active window.
            ApplyPendingResize();

            if (!_isActive) return;

            //Exit the game when pressing escape
            if (Input.WasKeyPressed(Keys.Escape))
                Exit();

            _screenManager.Update(gameTime, _isActive);

            //BEPU Physics v2
            if (!GameSettings.e_enableeditor && GameSettings.p_physics)
                _physics.Step((float)gameTime.ElapsedGameTime.TotalSeconds);

            // TODO: Add your update logic here

            //base.Update(gameTime);
        }

        /// <summary>
        /// This is called when the game should draw itself.
        /// </summary>
        /// <param name="gameTime">Provides a snapshot of timing values.</param>
        protected override void Draw(GameTime gameTime)
        {
            //Don't draw when the game is not running
            if (!_isActive)
            {
                Thread.Sleep(20);
                return;
            }

            CheckFPSLimitChange();

            _screenManager.Draw(gameTime);

            //GraphicsDevice.Clear(Color.CornflowerBlue);

            // TODO: Add your drawing code here

            //base.Draw(gameTime);
        }

        private void CheckFPSLimitChange()
        {
            if (_vsync != GameSettings.g_vsync || _fixFPS != GameSettings.g_fixedfps)
            {

                SetFPSLimit();
                _vsync = GameSettings.g_vsync;
                _fixFPS = GameSettings.g_fixedfps;
            }
        }

        private void SetFPSLimit()
        {
            if (!GameSettings.g_vsync && GameSettings.g_fixedfps <= 0)
            {
                _graphics.SynchronizeWithVerticalRetrace = false;
                IsFixedTimeStep = false;
                _graphics.ApplyChanges();
            }
            else
            {
                if (GameSettings.g_fixedfps > 0)
                {
                    _graphics.SynchronizeWithVerticalRetrace = false;
                    IsFixedTimeStep = true;
                    TargetElapsedTime = TimeSpan.FromMilliseconds(1000.0f / GameSettings.g_fixedfps);
                }
                else //Vsync
                {
                    _graphics.SynchronizeWithVerticalRetrace = true;
                    IsFixedTimeStep = false;
                    _graphics.ApplyChanges();
                }
            }
        }

        private void IsActivated(object sender, EventArgs e)
        {
            _isActive = true;
        }

        private void IsDeactivated(object sender, EventArgs e)
        {
            _isActive = false;
        }

        /// <summary>
        /// Update rendertargets and backbuffer when resizing window size
        /// </summary>
        private void ClientChangedWindowSize(object sender, EventArgs e)
        {
            //This fires from inside MonoGame's WinFormsGameWindow.OnResize, i.e. inside the
            //WinForms WndProc callstack. Resetting the GraphicsDevice here (ApplyChanges)
            //NREs on the reparented Anvil child window, so we only flag the change and do
            //the actual work in ApplyPendingResize() on the next Update tick.
            _pendingResize = true;
        }

        /// <summary>
        /// Reconcile the backbuffer + every render target with the current window client
        /// size. Runs on the game thread at the top of Update (outside the resize WndProc),
        /// which is the only place ApplyChanges/Reset is safe on the embedded child window.
        /// </summary>
        private void ApplyPendingResize()
        {
            //Boot self-prime: for the first frames, force a reconcile whenever the actual client
            //bounds don't match the resolution we're rendering at. This catches the Anvil-hosted
            //case where the engine HWND is resized to the container before any ClientSizeChanged we
            //observe, so the viewport fits on startup instead of staying at the stale 640x480
            //default until the user drags the window.
            if (_bootReconcileTicks > 0)
            {
                _bootReconcileTicks--;
                GetRealClientSize(out int cw, out int ch);
                if (cw > 0 && ch > 0 &&
                    (cw != GameSettings.g_screenwidth || ch != GameSettings.g_screenheight))
                {
                    _pendingResize = true;
                }
            }

            if (!_pendingResize) return;

            //Use the real Win32 client rect, not MonoGame's cached ClientBounds — the latter can be
            //stale (640x480) when Anvil resizes the HWND externally, which would otherwise make us
            //"reconcile" to the wrong size.
            GetRealClientSize(out int w, out int h);

            //Ignore degenerate sizes (e.g. while minimized); keep the request pending.
            if (w <= 0 || h <= 0) return;

            //Debounce: only rebuild once the size has held steady for a tick, so a live drag
            //doesn't dispose+recreate every render target every frame. While it's still
            //changing we keep _pendingResize set and reconcile once it settles.
            if (w != _lastResizeW || h != _lastResizeH)
            {
                _lastResizeW = w;
                _lastResizeH = h;
                return;
            }

            _pendingResize = false;

            //Bail when the client size already matches the resolution we last rendered at.
            //Comparing against GameSettings (not the old, self-defeating Viewport-vs-
            //PreferredBackBuffer test) is what lets this fire for both the embedded editor
            //(HWND resized by MonoGameHost) and standalone user-drag.
            if (w == GameSettings.g_screenwidth && h == GameSettings.g_screenheight) return;

            EditorBridge.Log($"ApplyPendingResize -> realRect={w}x{h}, ClientBounds=" +
                $"{Window.ClientBounds.Width}x{Window.ClientBounds.Height}, " +
                $"backbuffer={GraphicsDevice.PresentationParameters.BackBufferWidth}x" +
                $"{GraphicsDevice.PresentationParameters.BackBufferHeight}");

            GameSettings.g_screenwidth = w;
            GameSettings.g_screenheight = h;

            //MonoGameHost pre-syncs PreferredBackBuffer so MonoGame's own OnResize handler
            //early-returns; this is the single place the swap chain is actually resized.
            _graphics.PreferredBackBufferWidth = w;
            _graphics.PreferredBackBufferHeight = h;
            _graphics.ApplyChanges();

            //Explicitly cover the full backbuffer so the final fullscreen blit isn't
            //clipped to a stale viewport.
            GraphicsDevice.Viewport = new Viewport(0, 0, w, h);

            //Rebuild every render target at the new resolution.
            _screenManager.UpdateResolution();
        }
    }
}
