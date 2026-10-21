using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Anvil.Controls;

/// <summary>A read-only reference label with an empty hint and an explicit clear action.</summary>
public partial class ReferenceSlot : UserControl
{
    private bool _initialized;
    public static readonly StyledProperty<string?> ValueProperty =
        AvaloniaProperty.Register<ReferenceSlot, string?>(nameof(Value));
    public static readonly StyledProperty<string> PlaceholderProperty =
        AvaloniaProperty.Register<ReferenceSlot, string>(nameof(Placeholder), "Drop a reference here");
    public static readonly RoutedEvent<RoutedEventArgs> ClearEvent =
        RoutedEvent.Register<ReferenceSlot, RoutedEventArgs>(nameof(Clear), RoutingStrategies.Bubble);

    public string? Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public string Placeholder { get => GetValue(PlaceholderProperty); set => SetValue(PlaceholderProperty, value); }
    public event EventHandler<RoutedEventArgs> Clear
    {
        add => AddHandler(ClearEvent, value);
        remove => RemoveHandler(ClearEvent, value);
    }

    public ReferenceSlot()
    {
        InitializeComponent();
        _initialized = true;
        Refresh();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValueProperty || change.Property == PlaceholderProperty) Refresh();
    }

    private void Refresh()
    {
        if (!_initialized) return;
        if (this.FindControl<TextBlock>("SlotLabel") is not { } label) return;
        bool empty = string.IsNullOrEmpty(Value);
        label.Text = empty ? Placeholder : Value;
        label.Opacity = empty ? 0.55 : 1;
        this.FindControl<Button>("ClearButton")!.IsVisible = !empty;
    }

    public void SetDropTarget(bool active) => this.FindControl<Border>("SlotBorder")!.Classes.Set("dropTarget", active);

    private void Clear_Click(object? sender, RoutedEventArgs e)
    {
        RaiseEvent(new RoutedEventArgs(ClearEvent));
        e.Handled = true;
    }
}
