using System;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Anvil.Controls;

/// <summary>Compact heading help: hover or click (including keyboard activation) to read a wrapped tooltip.</summary>
public sealed class HelpButton : Button
{
    public static readonly StyledProperty<string> DescriptionProperty =
        AvaloniaProperty.Register<HelpButton, string>(nameof(Description), "");

    private readonly TextBlock _helpText = new() { MaxWidth = 360, TextWrapping = TextWrapping.Wrap, FontSize = 12 };

    protected override Type StyleKeyOverride => typeof(Button);

    public string Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public HelpButton()
    {
        Classes.Add("iconButton");
        Content = "?";
        Width = Height = 20;
        MinWidth = MinHeight = 0;
        Padding = new Thickness(0);
        FontSize = 12;
        FontWeight = FontWeight.SemiBold;
        VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetName(this, "Show help");
        ToolTip.SetTip(this, _helpText);
        ToolTip.SetPlacement(this, PlacementMode.Bottom);
        ToolTip.SetShowDelay(this, 250);
        Click += (_, e) =>
        {
            ToolTip.SetIsOpen(this, true);
            e.Handled = true; // A help click must not toggle the surrounding Expander header.
        };
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != DescriptionProperty) return;
        _helpText.Text = Description;
        AutomationProperties.SetHelpText(this, Description);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            ToolTip.SetIsOpen(this, false);
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        ToolTip.SetIsOpen(this, false);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ToolTip.SetIsOpen(this, false);
        base.OnDetachedFromVisualTree(e);
    }
}
