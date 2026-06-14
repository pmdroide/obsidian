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

    // Avalonia owns and sizes this intermediate container (a plain STATIC window) as the
    // native control. The engine HWND is reparented UNDER it and is resized only by us, so
    // MonoGame never receives an Avalonia-driven cross-thread resize — whose in-WndProc
    // GraphicsDevice.Reset NREs on the reparented child window.
    private IntPtr _containerHwnd;
    private Size _appliedSize;

    // Boot watchdog: during MonoGame's device/window init, MonoGame forces the engine window's
    // client size back to its PreferredBackBuffer default (640x480) on a deferred tick — AFTER our
    // last ArrangeOverride already sized it to the container. That self-shrink only re-fires
    // ArrangeOverride if Avalonia happens to do another layout pass, which it usually doesn't, so
    // the viewport stays shrunk (white borders) until a manual resize. This timer re-asserts the
    // container size for a short window after attach, catching the shrink and moving the engine back.
    private DispatcherTimer? _bootResizeWatchdog;
    private int _bootWatchdogTicksLeft;

    // Runtime resize settle: Avalonia applies the container HWND's new geometry AFTER our
    // ArrangeOverride returns (the NativeControlHost positions its child post-arrange). During an
    // incremental drag the continuous stream of layout passes hides that one-pass lag, but a
    // DISCRETE jump — maximize, restore, OS fullscreen — is a single arrange whose synchronous
    // container read still sees the OLD size, and no follow-up layout pass fires to correct it, so
    // the engine stays shrunk (white borders) in the now-larger window. This timer re-reads the
    // container for a short window after each layout change, catching its final size once Avalonia
    // (and Win32) have applied it. ResizeEngineToContainer no-ops once the engine already matches,
    // so the timer settles to cheap no-ops and self-stops.
    private DispatcherTimer? _resizeSettleTimer;
    private int _resizeSettleTicksLeft;

    // Parent window we subscribe to for state-change / activation events, so we can re-assert
    // keyboard focus after a maximize/fullscreen/restore (see RestoreEditorKeyboardFocus).
    private Window? _parentWindow;

    // Name of a plain managed focus sink declared in MainWindow.axaml next to the viewport.
    // We re-focus IT (not this NativeControlHost) after a window-state change: focusing the host
    // hands OS focus to the reparented engine child, where MonoGame's keyboard reads empty and
    // Avalonia stops raising KeyDown — so forwarding would stay dead. A managed sink keeps OS
    // focus with Avalonia, which is the only state in which TopLevel KeyDown forwarding works.
    private const string KeyboardSinkName = "ViewportKeyboardSink";

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
    private const uint WS_CLIPCHILDREN = 0x02000000;
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

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(uint dwExStyle, string lpClassName, string? lpWindowName,
        uint dwStyle, int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);

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

        // Watch the parent window so we can restore keyboard focus after a maximize/
        // fullscreen/restore (which clears the focused element and silences key forwarding).
        if (TopLevel.GetTopLevel(this) is Window w)
        {
            _parentWindow = w;
            w.PropertyChanged += OnWindowPropertyChanged;
            w.Activated += OnWindowActivated;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_parentWindow != null)
        {
            _parentWindow.PropertyChanged -= OnWindowPropertyChanged;
            _parentWindow.Activated -= OnWindowActivated;
            _parentWindow = null;
        }
        base.OnDetachedFromVisualTree(e);
    }

    private void OnWindowActivated(object? sender, EventArgs e)
    {
        // Alt-tabbing back can also leave nothing focused; re-assert over a short window.
        NudgeResizeSettle();
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != Window.WindowStateProperty) return;

        // A maximize/fullscreen/restore transition typically drops in-flight key events, so any
        // key held across it never gets its KeyUp — release them now so the camera doesn't drift.
        (_game?.Bridge as EditorBridge)?.ClearHostKeys();

        // Avalonia clears the focused element slightly AFTER raising this change, so a single
        // immediate re-focus can fire too early and see stale (non-null) focus. Reuse the settle
        // timer to retry focus restoration across the transition (it also re-asserts size, a
        // no-op once the engine already matches).
        NudgeResizeSettle();
    }

    // Re-assert keyboard focus on a plain managed sink when no managed element holds focus — the
    // post-fullscreen state in which KeyDown is no longer raised and host-key forwarding goes
    // silent. When something IS focused (e.g. the user is editing an inspector field) we leave it
    // untouched, so this never steals focus mid-edit.
    private void RestoreEditorKeyboardFocus()
    {
        if (_disposed) return;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        // GetFocusedElement() returning this host means OS focus is in the engine child (forwarding
        // dead); null means nothing is focused (forwarding dead). Either way we must hand focus to a
        // managed control. Any OTHER non-null managed element already routes KeyDown — leave it.
        var focused = topLevel.FocusManager?.GetFocusedElement();
        if (focused != null && !ReferenceEquals(focused, this)) return;

        var sink = _parentWindow?.FindControl<Control>(KeyboardSinkName);
        if (sink == null)
        {
            EditorBridge.Log($"RestoreEditorKeyboardFocus: sink '{KeyboardSinkName}' not found");
            return;
        }

        bool ok = sink.Focus();
        EditorBridge.Log($"RestoreEditorKeyboardFocus: prevFocus={focused?.GetType().Name ?? "null"}, " +
            $"sink.Focus()={ok}, nowFocus={topLevel.FocusManager?.GetFocusedElement()?.GetType().Name ?? "null"}");
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

        // Size the initial geometry from the host's real client rect (Bounds is 0 before the
        // first layout pass).
        int cw, ch;
        if (GetClientRect(_hostHwnd, out var hostRect) && hostRect.Right > 1 && hostRect.Bottom > 1)
        {
            cw = hostRect.Right - hostRect.Left;
            ch = hostRect.Bottom - hostRect.Top;
        }
        else
        {
            cw = Math.Max(1, (int)Bounds.Width);
            ch = Math.Max(1, (int)Bounds.Height);
        }

        // Create an intermediate container under the HWND Avalonia gave us, and hand THAT
        // back as the native control. Avalonia resizes the returned handle on every layout
        // pass via a cross-thread SetWindowPos; since the container is a plain STATIC window
        // with no graphics device, those resizes are harmless. The engine HWND lives under it
        // and is resized only by us (ResizeEngineToContainer) with PreferredBackBuffer
        // pre-synced — so MonoGame never resets the device from inside that WndProc.
        _containerHwnd = CreateWindowEx(0, "STATIC", null,
            WS_CHILD | WS_VISIBLE | WS_CLIPCHILDREN, 0, 0, cw, ch,
            _hostHwnd, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

        if (_containerHwnd == IntPtr.Zero)
        {
            // Degraded fallback: parent the engine directly under the host (the pre-container
            // behaviour). Resize crashes can recur here, but STATIC creation effectively never
            // fails, so this is just a safety net.
            EditorBridge.Log("MonoGameHost: container HWND creation failed; parenting engine directly");
            _containerHwnd = _hostHwnd;
        }

        ReparentGameWindow(_containerHwnd);

        StartBootResizeWatchdog();

        IntPtr returned = _containerHwnd != _hostHwnd ? _containerHwnd : _gameHwnd;
        return new PlatformHandle(returned, "HWND");
    }

    // Re-assert the container size on the UI thread for a short window after boot. MonoGame's
    // deferred init shrinks the engine window to 640x480 ~100ms in; this catches it and resizes
    // the engine back to the container. ResizeEngineToContainer compares the engine's REAL client
    // rect, so once the size sticks at the container the timer's calls become no-ops, and the timer
    // self-stops after its budget.
    private void StartBootResizeWatchdog()
    {
        _bootWatchdogTicksLeft = 60; // ~3s at 50ms ticks — covers MonoGame's deferred resize.
        _bootResizeWatchdog = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _bootResizeWatchdog.Tick += (_, _) =>
        {
            if (_disposed || --_bootWatchdogTicksLeft <= 0)
            {
                _bootResizeWatchdog?.Stop();
                _bootResizeWatchdog = null;
                return;
            }
            ResizeEngineToContainer();
        };
        _bootResizeWatchdog.Start();
    }

    private void ReparentGameWindow(IntPtr parentHwnd)
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

        SetParent(_gameHwnd, parentHwnd);

        // Fill the parent's client area. A 1x1 here would make the engine commit a 1x1
        // backbuffer, so fall back to Bounds only when the parent rect isn't ready.
        int w, h;
        if (GetClientRect(parentHwnd, out var rect) && rect.Right > 1 && rect.Bottom > 1)
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

        // Pre-sync PreferredBackBuffer to this size BEFORE the window resizes, so the WM_SIZE
        // raised below makes MonoGame's WinFormsGameWindow.OnResize early-return instead of
        // resetting the device mid-initialization (which NREs).
        SyncPreferredBackBuffer(w, h);

        SetWindowPos(_gameHwnd, IntPtr.Zero, 0, 0, w, h,
            SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED | SWP_SHOWWINDOW);
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        _disposed = true;

        try { _bootResizeWatchdog?.Stop(); } catch { }
        _bootResizeWatchdog = null;

        try { _resizeSettleTimer?.Stop(); } catch { }
        _resizeSettleTimer = null;

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
            // Destroy our container (the engine child is torn down with the game thread's
            // _game.Dispose above). Skip when it aliases the host HWND (degraded fallback).
            if (_containerHwnd != IntPtr.Zero && _containerHwnd != _hostHwnd)
            {
                try { DestroyWindow(_containerHwnd); } catch { }
            }
            _containerHwnd = IntPtr.Zero;

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
            // a public field on Engine.Engine. We only use it to keep PreferredBackBuffer
            // in lockstep with the child's client size (see ApplyChildSize) so MonoGame's
            // own OnResize handler early-returns — we never call ApplyChanges from here.
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
                    // Safety net: a stray resize that slips through with a stale
                    // PreferredBackBuffer can NRE inside MonoGame's GraphicsDevice.Reset.
                    // Swallow it here so it never kills the app; the engine reconciles the
                    // device on its next Update (Engine.ApplyPendingResize).
                    try { DispatchMessage(ref msg); }
                    catch (Exception ex) { EditorBridge.Log("DispatchMessage threw: " + ex); }
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
        // Avalonia sizes the container window (the handle we returned) here, on the UI thread.
        var result = base.ArrangeOverride(finalSize);
        // Synchronous attempt — correct for the common incremental-drag case where the container
        // already carries its new size by the time we read it.
        ResizeEngineToContainer();
        // ...then re-assert for a short settle window so the engine still catches up after a
        // maximize/fullscreen jump, whose container resize lands AFTER this arrange pass.
        NudgeResizeSettle();
        return result;
    }

    // (Re)start the runtime resize-settle timer. Called on every layout change; resetting the tick
    // budget keeps it alive across a live drag and lets it settle ~240ms after the last change.
    private void NudgeResizeSettle()
    {
        if (_disposed) return;
        _resizeSettleTicksLeft = 8; // ~240ms at 30ms ticks — covers the deferred container resize.
        if (_resizeSettleTimer != null) return;
        _resizeSettleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        _resizeSettleTimer.Tick += (_, _) =>
        {
            if (_disposed || --_resizeSettleTicksLeft <= 0)
            {
                _resizeSettleTimer?.Stop();
                _resizeSettleTimer = null;
                return;
            }
            ResizeEngineToContainer();
            // A window-state change also lands here (via OnWindowPropertyChanged). Retrying the
            // focus restore across the settle window catches the moment Avalonia clears focus.
            RestoreEditorKeyboardFocus();
        };
        _resizeSettleTimer.Start();
    }

    // Resize the engine HWND to exactly fill the container's client area. We read the
    // container's ACTUAL client rect (physical pixels) and use that identical size for both
    // PreferredBackBuffer and MoveWindow. MoveWindow's WM_SIZE is handled on the game thread by
    // MonoGame's OnResize, which then sees ClientSize == PreferredBackBuffer and early-returns
    // instead of resetting the device (the reset NREs on this reparented child window). No DPI
    // rounding guesswork — the same integers are used on both sides.
    private void ResizeEngineToContainer()
    {
        if (_gameHwnd == IntPtr.Zero || _containerHwnd == IntPtr.Zero) return;
        if (!GetClientRect(_containerHwnd, out var rect)) return;

        int w = rect.Right - rect.Left;
        int h = rect.Bottom - rect.Top;
        if (w <= 0 || h <= 0) return;

        // Compare against the engine HWND's ACTUAL client rect, not our cached _appliedSize.
        // During boot MonoGame's own device/window init forces the engine window's client size
        // back to its PreferredBackBuffer default (640x480) AFTER we already sized it to the
        // container — a genuine WM_SIZE the engine reconciles to, which is what shrank the
        // viewport (white borders). If we trusted _appliedSize we'd early-return and never undo
        // that shrink. Reading the real engine rect lets us re-assert the container size whenever
        // anything (MonoGame included) moves the engine away from it.
        if (GetClientRect(_gameHwnd, out var engineRect))
        {
            int ew = engineRect.Right - engineRect.Left;
            int eh = engineRect.Bottom - engineRect.Top;
            if (ew == w && eh == h) { _appliedSize = new Size(w, h); return; }
        }
        _appliedSize = new Size(w, h);

        SyncPreferredBackBuffer(w, h);
        MoveWindow(_gameHwnd, 0, 0, w, h, true);
        // The engine rebuilds its backbuffer + render targets itself, on the game thread,
        // once it sees the new client size (Engine.ApplyPendingResize) — debounced there so a
        // live drag doesn't rebuild every frame.
    }

    // Set PreferredBackBuffer without ever calling ApplyChanges — that (a device Reset) must
    // only ever happen on the game thread, which the engine does itself in Engine.Update.
    private void SyncPreferredBackBuffer(int w, int h)
    {
        if (_graphicsManager == null) return;
        _graphicsManager.PreferredBackBufferWidth = w;
        _graphicsManager.PreferredBackBufferHeight = h;
    }
}
