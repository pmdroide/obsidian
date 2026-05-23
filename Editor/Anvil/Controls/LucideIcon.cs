using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Anvil.Controls;

/// <summary>
/// Renders a Lucide icon from the merged ResourcesIcons.axaml dictionary
/// with a configurable stroke color and thickness. Works around the
/// LucideAvalonia.Lucide control crashing on Avalonia 12 (RelativeSource ctor
/// signature change).
/// </summary>
public class LucideIcon : Control
{
    public static readonly StyledProperty<string?> IconProperty =
        AvaloniaProperty.Register<LucideIcon, string?>(nameof(Icon));

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<LucideIcon, IBrush?>(nameof(Foreground), Brushes.Gray);

    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<LucideIcon, double>(nameof(StrokeThickness), 1.6);

    public string? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    static LucideIcon()
    {
        AffectsRender<LucideIcon>(IconProperty, ForegroundProperty, StrokeThicknessProperty);
    }

    public override void Render(DrawingContext context)
    {
        var name = Icon;
        if (string.IsNullOrEmpty(name)) return;

        if (!TryLookupIcon(name, out var raw) || raw is not DrawingImage di || di.Drawing is not { } drawing)
            return;

        var bounds = Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        // Lucide source viewBox is 24x24.
        const double viewBox = 24.0;
        var scale = Math.Min(bounds.Width / viewBox, bounds.Height / viewBox);
        var dx = (bounds.Width - viewBox * scale) * 0.5;
        var dy = (bounds.Height - viewBox * scale) * 0.5;

        // Scale stroke up so its on-screen width matches StrokeThickness regardless of icon size.
        var strokeInSourceUnits = scale > 0 ? StrokeThickness / scale : StrokeThickness;
        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(dx, dy)))
        {
            DrawRecursive(context, drawing, strokeInSourceUnits);
        }
    }

    private void DrawRecursive(DrawingContext context, Drawing drawing, double strokeWidth)
    {
        switch (drawing)
        {
            case DrawingGroup group:
                foreach (var child in group.Children)
                    DrawRecursive(context, child, strokeWidth);
                break;

            case GeometryDrawing gd when gd.Geometry is { } geom:
            {
                var brush = Foreground;
                Pen? pen = null;
                if (gd.Pen != null)
                {
                    pen = new Pen(brush, strokeWidth,
                        lineCap: gd.Pen.LineCap,
                        lineJoin: gd.Pen.LineJoin);
                }
                else if (brush != null)
                {
                    pen = new Pen(brush, strokeWidth, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
                }
                context.DrawGeometry(null, pen, geom);
                break;
            }
        }
    }

    private bool TryLookupIcon(string name, out object? value)
    {
        if (Avalonia.Controls.ResourceNodeExtensions.TryFindResource(this, name, out value))
            return true;
        var app = Application.Current;
        if (app != null && Avalonia.Controls.ResourceNodeExtensions.TryFindResource(app, name, out value))
            return true;
        value = null;
        return false;
    }
}
