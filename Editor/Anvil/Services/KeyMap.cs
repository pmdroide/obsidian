using Avalonia.Input;
using XnaKeys = Microsoft.Xna.Framework.Input.Keys;

namespace Anvil.Services;

/// <summary>
/// Avalonia keys to XNA keys. <see cref="Controls.MonoGameHost"/> forwards every mapped key to the engine
/// (the engine HWND never has keyboard focus in Anvil), and the Input Actions window uses it to listen for
/// a binding. Covers the whole keyboard of <see cref="Engine.Logic.InputControls"/>; anything else is
/// <see cref="XnaKeys.None"/>.
/// </summary>
public static class KeyMap
{
    public static XnaKeys ToXna(Key k)
    {
        // Contiguous ranges in both enums.
        if (k >= Key.A && k <= Key.Z) return XnaKeys.A + (k - Key.A);
        if (k >= Key.D0 && k <= Key.D9) return XnaKeys.D0 + (k - Key.D0);
        if (k >= Key.NumPad0 && k <= Key.NumPad9) return XnaKeys.NumPad0 + (k - Key.NumPad0);
        if (k >= Key.F1 && k <= Key.F24) return XnaKeys.F1 + (k - Key.F1);
        return k switch
        {
            Key.Space => XnaKeys.Space,
            Key.Enter => XnaKeys.Enter,
            Key.Escape => XnaKeys.Escape,
            Key.Tab => XnaKeys.Tab,
            Key.Back => XnaKeys.Back,
            Key.LeftShift => XnaKeys.LeftShift, Key.RightShift => XnaKeys.RightShift,
            Key.LeftCtrl => XnaKeys.LeftControl, Key.RightCtrl => XnaKeys.RightControl,
            Key.LeftAlt => XnaKeys.LeftAlt, Key.RightAlt => XnaKeys.RightAlt,
            Key.LWin => XnaKeys.LeftWindows, Key.RWin => XnaKeys.RightWindows, Key.Apps => XnaKeys.Apps,
            Key.Up => XnaKeys.Up, Key.Down => XnaKeys.Down, Key.Left => XnaKeys.Left, Key.Right => XnaKeys.Right,
            Key.Insert => XnaKeys.Insert, Key.Delete => XnaKeys.Delete,
            Key.Home => XnaKeys.Home, Key.End => XnaKeys.End,
            Key.PageUp => XnaKeys.PageUp, Key.PageDown => XnaKeys.PageDown,
            Key.CapsLock => XnaKeys.CapsLock, Key.NumLock => XnaKeys.NumLock, Key.Scroll => XnaKeys.Scroll,
            Key.PrintScreen => XnaKeys.PrintScreen, Key.Pause => XnaKeys.Pause,
            Key.OemTilde => XnaKeys.OemTilde, Key.OemMinus => XnaKeys.OemMinus, Key.OemPlus => XnaKeys.OemPlus,
            Key.OemOpenBrackets => XnaKeys.OemOpenBrackets, Key.OemCloseBrackets => XnaKeys.OemCloseBrackets,
            Key.OemPipe => XnaKeys.OemPipe, Key.OemSemicolon => XnaKeys.OemSemicolon, Key.OemQuotes => XnaKeys.OemQuotes,
            Key.OemComma => XnaKeys.OemComma, Key.OemPeriod => XnaKeys.OemPeriod, Key.OemQuestion => XnaKeys.OemQuestion,
            Key.OemBackslash => XnaKeys.OemBackslash,
            Key.Add => XnaKeys.Add, Key.Subtract => XnaKeys.Subtract, Key.Multiply => XnaKeys.Multiply,
            Key.Divide => XnaKeys.Divide, Key.Decimal => XnaKeys.Decimal,
            _ => XnaKeys.None,
        };
    }
}
