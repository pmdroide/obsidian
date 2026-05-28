using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Threading;
using Engine.Editor;
using Microsoft.Xna.Framework;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using XnaKeys = Microsoft.Xna.Framework.Input.Keys;

namespace Anvil.Controls;

/// <summary>
/// Hosts the MonoGame WindowsDX engine as a child of an Avalonia NativeControlHost
/// by reparenting the MonoGame form's HWND under the parent HWND Avalonia provides.
/// </summary>
public class MonoGameHost : NativeControlHost
{
    private Thread? _gameThread;
    private Engine.Engine? _game;
    private GraphicsDeviceManager? _graphicsManager;
    private IntPtr _gameHwnd;
    private IntPtr _hostHwnd;
    private readonly ManualResetEventSlim _handleReady = new(false);
    private string? _previousCwd;
    private volatile bool _disposed;

    private DispatcherTimer? _resizeTimer;
    private Size _pendingSize;
    private Size _appliedSize;
    private bool _initialResizeDone;

    /// <summary>
    /// Fires on the UI thread once the embedded engine has constructed its
    /// editor bridge. Subscribe to wire engine ↔ Anvil view-model traffic.
    /// </summary>
    public event Action<IEditorBridge>? BridgeReady;

    public IEditorBridge? Bridge => _game?.Bridge;

    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;
    private const uint WS_CHILD = 0x40000000;
    private const uint WS_VISIBLE = 0x10000000;
    private const uint WS_POPUP = 0x80000000;
    private const uint WS_CAPTION = 0x00C00000;
    private const uint WS_THICKFRAME = 0x00040000;
    private const uint WS_MINIMIZEBOX = 0x00020000;
    private const uint WS_MAXIMIZEBOX = 0x00010000;
    private const uint WS_SYSMENU = 0x00080000;
    private const uint WS_DLGFRAME = 0x00400000;
    private const uint WS_BORDER = 0x00800000;
    private const uint WS_EX_APPWINDOW = 0x00040000;
    private const uint WS_EX_TOOLWINDOW = 0x00000080;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const uint SWP_SHOWWINDOW = 0x0040;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    private const uint PM_REMOVE = 1;

    /// <summary>
    /// Subscribes the parent Window to KeyDown/KeyUp events the first time the
    /// engine HWND attaches. Listening at window level means the engine sees
    /// WASD even when focus is on the toolbar or any other Avalonia control —
    /// the reparented engine HWND never receives keyboard focus directly.
    /// </summary>
    private void EnsureKeyForwarding()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;
        topLevel.AddHandler(InputElement.KeyDownEvent, OnTopLevelKeyDown, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
        topLevel.AddHandler(InputElement.KeyUpEvent, OnTopLevelKeyUp, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
    }

    private void OnTopLevelKeyDown(object? sender, KeyEventArgs e)
    {
        if (_game?.Bridge is not EditorBridge b) return;
        XnaKeys xna = MapAvaloniaKey(e.Key);
        if (xna != XnaKeys.None) b.SetHostKeyState((int)xna, true);
    }

    private void OnTopLevelKeyUp(object? sender, KeyEventArgs e)
    {
        if (_game?.Bridge is not EditorBridge b) return;
        XnaKeys xna = MapAvaloniaKey(e.Key);
        if (xna != XnaKeys.None) b.SetHostKeyState((int)xna, false);
    }

    /// <summary>
    /// Map the subset of Avalonia keys the engine cares about (WASD/QE camera
    /// fly, Shift/Ctrl modifiers, F1 render-mode cycle, Space editor toggle).
    /// Everything else returns <see cref="XnaKeys.None"/> and is ignored.
    /// </summary>
    private static XnaKeys MapAvaloniaKey(Key k) => k switch
    {
        Key.A => XnaKeys.A, Key.B => XnaKeys.B, Key.C => XnaKeys.C, Key.D => XnaKeys.D,
        Key.E => XnaKeys.E, Key.F => XnaKeys.F, Key.G => XnaKeys.G, Key.H => XnaKeys.H,
        Key.I => XnaKeys.I, Key.J => XnaKeys.J, Key.K => XnaKeys.K, Key.L => XnaKeys.L,
        Key.M => XnaKeys.M, Key.N => XnaKeys.N, Key.O => XnaKeys.O, Key.P => XnaKeys.P,
        Key.Q => XnaKeys.Q, Key.R => XnaKeys.R, Key.S => XnaKeys.S, Key.T => XnaKeys.T,
        Key.U => XnaKeys.U, Key.V => XnaKeys.V, Key.W => XnaKeys.W, Key.X => XnaKeys.X,
        Key.Y => XnaKeys.Y, Key.Z => XnaKeys.Z,
        Key.Space => XnaKeys.Space,
        Key.LeftShift => XnaKeys.LeftShift, Key.RightShift => XnaKeys.RightShift,
        Key.LeftCtrl => XnaKeys.LeftControl, Key.RightCtrl => XnaKeys.RightControl,
        Key.LeftAlt => XnaKeys.LeftAlt, Key.RightAlt => XnaKeys.RightAlt,
        Key.F1 => XnaKeys.F1, Key.F2 => XnaKeys.F2, Key.F3 => XnaKeys.F3,
        Key.Escape => XnaKeys.Escape,
        _ => XnaKeys.None,
    };

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // TopLevel is reliably non-null once attached; subscribing in
        // CreateNativeControlCore is sometimes too early.
        EnsureKeyForwarding();
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        _hostHwnd = parent.Handle;

        // MonoGame loads Content/ relative to the working directory. Anvil is the
        // entry point, so set CWD to the Engine assembly's directory before booting.
        var engineDir = Path.GetDirectoryName(typeof(Engine.Engine).Assembly.Location);
        if (!string.IsNullOrEmpty(engineDir))
        {
            _previousCwd = Environment.CurrentDirectory;
            Environment.CurrentDirectory = engineDir;
        }

        _gameThread = new Thread(GameThreadProc)
        {
            IsBackground = true,
            Name = "MonoGameEngineThread",
        };
        _gameThread.SetApartmentState(ApartmentState.STA);
        _gameThread.Start();

        if (!_handleReady.Wait(TimeSpan.FromSeconds(10)) || _gameHwnd == IntPtr.Zero)
        {
            // Boot timed out; return a stub handle so Avalonia doesn't crash.
            return base.CreateNativeControlCore(parent);
        }

        ReparentGameWindow();
        return new PlatformHandle(_gameHwnd, "HWND");
    }

    private void ReparentGameWindow()
    {
        const uint topLevelMask = WS_CAPTION | WS_THICKFRAME | WS_MINIMIZEBOX
            | WS_MAXIMIZEBOX | WS_SYSMENU | WS_POPUP | WS_DLGFRAME | WS_BORDER;

        var style = (uint)GetWindowLong(_gameHwnd, GWL_STYLE);
        style &= ~topLevelMask;
        style |= WS_CHILD | WS_VISIBLE;
        SetWindowLong(_gameHwnd, GWL_STYLE, unchecked((int)style));

        var exStyle = (uint)GetWindowLong(_gameHwnd, GWL_EXSTYLE);
        exStyle &= ~WS_EX_APPWINDOW;
        exStyle |= WS_EX_TOOLWINDOW;
        SetWindowLong(_gameHwnd, GWL_EXSTYLE, unchecked((int)exStyle));

        SetParent(_gameHwnd, _hostHwnd);

        // Prefer the parent HWND's actual client rect — Bounds is 0 on first call
        // (layout hasn't run yet). A 1x1 here would make the engine's resize handler
        // commit a 1x1 backbuffer and then silently ignore later size changes.
        int w, h;
        if (GetClientRect(_hostHwnd, out var rect) && rect.Right > 1 && rect.Bottom > 1)
        {
            w = rect.Right - rect.Left;
            h = rect.Bottom - rect.Top;
        }
        else
        {
            w = Math.Max(1, (int)Bounds.Width);
            h = Math.Max(1, (int)Bounds.Height);
        }
        _appliedSize = new Size(w, h);
        SetWindowPos(_gameHwnd, IntPtr.Zero, 0, 0, w, h,
            SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED | SWP_SHOWWINDOW);
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        _disposed = true;
        _resizeTimer?.Stop();
        _resizeTimer = null;

        try
        {
            _gameThread?.Join(TimeSpan.FromSeconds(3));
        }
        catch
        {
            // Swallow shutdown errors — host is going away.
        }
        finally
        {
            if (_previousCwd != null)
            {
                try { Environment.CurrentDirectory = _previousCwd; } catch { }
            }
        }
    }

    private void GameThreadProc()
    {
        try
        {
            EditorBridge.Log("MonoGameHost: constructing Engine.Engine");
            _game = new Engine.Engine();
            EditorBridge.Log("MonoGameHost: Engine.Engine constructed");

            // GraphicsDeviceManager registers itself as IGraphicsDeviceManager in
            // Game.Services during its constructor, so this resolves without forcing
            // a public field on Engine.Engine.
            _graphicsManager = _game.Services.GetService<IGraphicsDeviceManager>() as GraphicsDeviceManager;

            // The MonoGame WinForms window is created during the Game constructor;
            // touching Window.Handle here also forces handle realization if needed.
            _gameHwnd = _game.Window?.Handle ?? IntPtr.Zero;
            _handleReady.Set();

            // Notify subscribers (on the UI thread) that the editor bridge is alive.
            // Bridge has been constructed by Engine but Bind() hasn't been called yet
            // — that happens during the first Initialize. Subscribers should be
            // tolerant of pre-Bind state; AvailableModelKeys will be empty until then.
            IEditorBridge? bridge = _game.Bridge;
            if (bridge != null)
            {
                // Tell the engine it is hosted in Anvil — gates the legacy in-engine
                // HelperSuite GUI off and lets ScreenManager pick the editor-friendly
                // defaults. Set BEFORE the first RunOneFrame below.
                (bridge as EditorBridge)?.SetHostedByEditor(true);

                Dispatcher.UIThread.Post(() =>
                {
                    try { BridgeReady?.Invoke(bridge); }
                    catch (Exception ex) { EditorBridge.Log("BridgeReady handler threw: " + ex); }
                });
            }
            else
            {
                EditorBridge.Log("MonoGameHost: _game.Bridge is null after construction");
            }

            // Manual game loop: Game.Run() pumps a blocking WinForms message loop on
            // this thread which deadlocks against Avalonia's UI thread. RunOneFrame
            // ticks Update/Draw without owning the message pump, so we drive it
            // ourselves and yield via Sleep(1) to keep CPU sane.
            while (!_disposed)
            {
                // 1. PUMP WIN32 MESSAGES
                // This answers the calls from Avalonia's UI thread (SetParent, MoveWindow)
                // preventing the entire application from deadlocking.
                while (PeekMessage(out MSG msg, IntPtr.Zero, 0, 0, PM_REMOVE))
                {
                    TranslateMessage(ref msg);
                    DispatchMessage(ref msg);
                }

                // 2. TICK MONOGAME
                try
                {
                    _game.RunOneFrame();
                }
                catch (Exception ex)
                {
                    // Swallow per-frame errors, but log so the user can diagnose
                    // an "Add Object crashes" sort of regression.
                    EditorBridge.Log("RunOneFrame threw: " + ex);
                }
                
                Thread.Sleep(1);
            }
        }
        catch (Exception ex)
        {
            // Engine threw during construction — release the waiter so the UI
            // thread doesn't hang forever in CreateNativeControlCore.
            EditorBridge.Log("GameThreadProc fatal: " + ex);
            _handleReady.Set();
        }
        finally
        {
            try { _game?.Dispose(); } catch { }
        }
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var result = base.ArrangeOverride(finalSize);
        if (_gameHwnd == IntPtr.Zero) return result;

        // First layout pass: resize synchronously so the engine never sees a stale
        // 1x1 size (which would trap its resize handler — see field comment).
        if (!_initialResizeDone)
        {
            _initialResizeDone = true;
            ApplyChildSize(finalSize);
            return result;
        }

        // Subsequent layout passes (the user is dragging the window) get debounced:
        // resizing on every pass would have the engine rebuild its backbuffer +
        // render targets dozens of times per second, which leaves the renderer blank.
        ScheduleResize(finalSize);
        return result;
    }

    private void ApplyChildSize(Size size)
    {
        var w = Math.Max(1, (int)size.Width);
        var h = Math.Max(1, (int)size.Height);
        if (_appliedSize.Width == w && _appliedSize.Height == h) return;
        _appliedSize = new Size(w, h);
        MoveWindow(_gameHwnd, 0, 0, w, h, true);

        // MoveWindow alone only resizes the HWND — the DX swap chain stays at its
        // old dimensions, so the engine keeps stretching an old backbuffer over the
        // new client area and looks frozen. Push the new size through the manager
        // to force a swap-chain rebuild at the right resolution.
        if (_graphicsManager != null)
        {
            _graphicsManager.PreferredBackBufferWidth = w;
            _graphicsManager.PreferredBackBufferHeight = h;
            try { _graphicsManager.ApplyChanges(); } catch { }
        }
    }

    private void ScheduleResize(Size finalSize)
    {
        _pendingSize = finalSize;
        if (_resizeTimer == null)
        {
            _resizeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            _resizeTimer.Tick += OnResizeTimerTick;
        }
        _resizeTimer.Stop();
        _resizeTimer.Start();
    }

    private void OnResizeTimerTick(object? sender, EventArgs e)
    {
        _resizeTimer?.Stop();
        if (_disposed || _gameHwnd == IntPtr.Zero) return;
        ApplyChildSize(_pendingSize);
    }
}
