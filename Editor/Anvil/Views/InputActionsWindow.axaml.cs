using System;
using System.ComponentModel;
using Anvil.Services;
using Anvil.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Engine.Logic;
using Microsoft.Xna.Framework.Input;
using XnaKeys = Microsoft.Xna.Framework.Input.Keys;

namespace Anvil.Views;

/// <summary>
/// Modal Input Actions editor (Window > Input Actions). Closes with <c>true</c> when the user saves.
/// While a slot listens, the next key (Esc cancels), mouse button or wheel notch over the overlay, or
/// gamepad button/trigger/stick direction (polled through XInput) becomes its control.
/// </summary>
public partial class InputActionsWindow : Window
{
    private readonly DispatcherTimer _padTimer = new() { Interval = TimeSpan.FromMilliseconds(30) };
    private readonly GamePadState[] _lastPads = new GamePadState[GamePad.MaximumGamePadCount];
    private InputActionsViewModel? _vm;

    public InputActionsWindow()
    {
        InitializeComponent();
        _padTimer.Tick += (_, _) => PollGamePads();
        // Tunnel, so a focused Button or TextBox never sees the key being bound (Space would click it).
        AddHandler(KeyDownEvent, OnListenKeyDown, RoutingStrategies.Tunnel);
        Closed += (_, _) => _padTimer.Stop();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm != null) _vm.PropertyChanged -= OnViewModelChanged;
        _vm = DataContext as InputActionsViewModel;
        if (_vm != null) _vm.PropertyChanged += OnViewModelChanged;
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(InputActionsViewModel.IsListening) || _vm == null) return;
        if (_vm.IsListening)
        {
            // Buttons already held when listening starts don't count.
            for (int i = 0; i < _lastPads.Length; i++) _lastPads[i] = ReadPad(i);
            _padTimer.Start();
            ListenOverlay.Focus();
        }
        else _padTimer.Stop();
    }

    private void OnListenKeyDown(object? sender, KeyEventArgs e)
    {
        if (_vm?.IsListening != true) return;
        e.Handled = true;
        if (e.Key == Key.Escape)
        {
            _vm.CancelListening();
            return;
        }
        XnaKeys key = KeyMap.ToXna(e.Key);
        if (key != XnaKeys.None) _vm.CompleteListening(InputControls.KeyPath(key));
    }

    private void ListenOverlay_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm?.IsListening != true) return;
        e.Handled = true;
        string? path = e.GetCurrentPoint(this).Properties.PointerUpdateKind switch
        {
            PointerUpdateKind.LeftButtonPressed => "Mouse/LeftButton",
            PointerUpdateKind.RightButtonPressed => "Mouse/RightButton",
            PointerUpdateKind.MiddleButtonPressed => "Mouse/MiddleButton",
            PointerUpdateKind.XButton1Pressed => "Mouse/XButton1",
            PointerUpdateKind.XButton2Pressed => "Mouse/XButton2",
            _ => null,
        };
        if (path != null) _vm.CompleteListening(path);
    }

    private void ListenOverlay_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (_vm?.IsListening != true || e.Delta.Y == 0) return;
        e.Handled = true;
        _vm.CompleteListening(e.Delta.Y > 0 ? "Mouse/ScrollUp" : "Mouse/ScrollDown");
    }

    private void PollGamePads()
    {
        if (_vm?.IsListening != true) return;
        for (int i = 0; i < _lastPads.Length; i++)
        {
            GamePadState now = ReadPad(i);
            string? path = InputControls.DetectGamePad(now, _lastPads[i]);
            _lastPads[i] = now;
            if (path == null) continue;
            _vm.CompleteListening(path);
            return;
        }
    }

    private static GamePadState ReadPad(int index)
    {
        try { return GamePad.GetState(index); }
        catch { return GamePadState.Default; }
    }

    private void Save_Click(object? sender, RoutedEventArgs e) => Close(true);

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        // Esc while listening only cancels the listen (handled above), never the dialog.
        if (_vm?.IsListening == true) return;
        Close(false);
    }
}
