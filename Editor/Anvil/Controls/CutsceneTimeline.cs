using System;
using System.Globalization;
using System.Linq;
using Anvil.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Engine.Logic;

namespace Anvil.Controls;

/// <summary>
/// Timeline of the Cutscenes window: a seconds ruler, the camera track (a diamond per key, a bar for a cut) and
/// the event track (a bar per event), with the playhead. Click or drag on empty space to scrub; drag a key or
/// event to move it in time; drag an event's right edge to lengthen or shorten it, its left edge to move its start
/// (the end stays); drag the end handle (after the last second) to make the cutscene longer or shorter. Drags snap
/// to 0.05 s, or 0.1 s with Shift. Draws <see cref="CutscenesViewModel"/>.
/// </summary>
public class CutsceneTimeline : Control
{
    private const double LabelWidth = 64;
    private const double RulerHeight = 22;
    private const double TrackHeight = 30;

    private static readonly IBrush Background = new SolidColorBrush(Color.Parse("#1A1A1A"));
    private static readonly IBrush TrackBrush = new SolidColorBrush(Color.Parse("#232323"));
    private static readonly IBrush LabelBrush = new SolidColorBrush(Color.Parse("#8A8A8A"));
    private static readonly IBrush TextBrush = new SolidColorBrush(Color.Parse("#E6E6E6"));
    private static readonly IBrush KeyBrush = new SolidColorBrush(Color.Parse("#4ED18A"));
    private static readonly IBrush CutBrush = new SolidColorBrush(Color.Parse("#F2B84B"));
    private static readonly IBrush SelectedBrush = Brushes.White;
    private static readonly IBrush EndBrush = new SolidColorBrush(Color.Parse("#5A5A5A"));
    private static readonly IBrush HandleBrush = new SolidColorBrush(Colors.White, 0.55);
    private static readonly IBrush PlayheadBrush = new SolidColorBrush(Color.Parse("#E06C5A"));
    private static readonly IPen TickPen = new Pen(new SolidColorBrush(Color.Parse("#3A3A3A")), 1);
    private static readonly IPen CurvePen = new Pen(new SolidColorBrush(Color.Parse("#4ED18A"), 0.35), 2);
    private static readonly IPen PlayheadPen = new Pen(new SolidColorBrush(Color.Parse("#E06C5A")), 1.5);
    private static readonly IPen SelectedPen = new Pen(Brushes.White, 1.5);
    private static readonly Typeface Face = new(FontFamily.Default);

    private const double EdgeGrab = 5;
    private static readonly Cursor ResizeCursor = new(StandardCursorType.SizeWestEast);
    private static readonly Cursor MoveCursor = new(StandardCursorType.Hand);

    private enum DragMode { None, Scrub, MoveKey, MoveEvent, EventStart, EventEnd, CutsceneEnd }

    private CutscenesViewModel? _vm;
    private DragMode _mode;
    private object? _dragging;
    private double _dragOffset;
    // Cutscene end drag: the scale at the press, so the track doesn't rescale under the pointer.
    private double _dragStartX, _dragStartDuration;

    public CutsceneTimeline()
    {
        ClipToBounds = true;
        Focusable = true;
        Height = RulerHeight + TrackHeight * 2 + 10;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm != null) _vm.TimelineChanged -= InvalidateVisual;
        _vm = DataContext as CutscenesViewModel;
        if (_vm != null) _vm.TimelineChanged += InvalidateVisual;
        InvalidateVisual();
    }

    private double Duration => Math.Max(0.1, _vm?.Duration ?? 10);
    private double TrackWidth => Math.Max(1, Bounds.Width - LabelWidth - 18);
    private double X(double time) => LabelWidth + time / Duration * TrackWidth;
    private double TimeAt(double x) => Math.Clamp((x - LabelWidth) / TrackWidth * Duration, 0, Duration);
    private static double CameraY => RulerHeight + 4;
    private static double EventsY => RulerHeight + 4 + TrackHeight;

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(Background, bounds);
        if (_vm == null || !_vm.HasCutscene)
        {
            DrawText(context, "Create or pick a cutscene", new Point(LabelWidth, RulerHeight + 20), LabelBrush, 12);
            return;
        }

        // Ruler: a tick per step, labelled.
        double step = PickStep(Duration, TrackWidth);
        for (double t = 0; t <= Duration + 1e-6; t += step)
        {
            double x = X(t);
            context.DrawLine(TickPen, new Point(x, RulerHeight - 6), new Point(x, bounds.Height));
            DrawText(context, t.ToString(step < 1 ? "0.0" : "0", CultureInfo.InvariantCulture) + "s", new Point(x + 3, 3), LabelBrush, 10);
        }

        // Tracks
        context.FillRectangle(TrackBrush, new Rect(LabelWidth, CameraY, TrackWidth, TrackHeight - 4));
        context.FillRectangle(TrackBrush, new Rect(LabelWidth, EventsY, TrackWidth, TrackHeight - 4));
        DrawText(context, "Camera", new Point(8, CameraY + 6), LabelBrush, 11);
        DrawText(context, "Events", new Point(8, EventsY + 6), LabelBrush, 11);

        var keys = _vm.Keys.OrderBy(k => k.Time).ToList();
        double mid = CameraY + (TrackHeight - 4) / 2;
        for (int i = 0; i + 1 < keys.Count; i++)
            if (!keys[i + 1].Cut)
                context.DrawLine(CurvePen, new Point(X(keys[i].Time), mid), new Point(X(keys[i + 1].Time), mid));
        foreach (var key in keys)
        {
            double x = X(key.Time);
            var diamond = new StreamGeometry();
            using (var g = diamond.Open())
            {
                g.BeginFigure(new Point(x, mid - 7), true);
                g.LineTo(new Point(x + 7, mid));
                g.LineTo(new Point(x, mid + 7));
                g.LineTo(new Point(x - 7, mid));
                g.EndFigure(true);
            }
            context.DrawGeometry(key.Cut ? CutBrush : KeyBrush, key.IsSelected ? SelectedPen : null, diamond);
        }

        foreach (var e in _vm.Events)
        {
            double x0 = X(e.Time);
            double width = e.UsesDuration ? Math.Max(6, e.Duration / Duration * TrackWidth) : 6;
            var rect = new Rect(x0, EventsY + 3, width, TrackHeight - 10);
            context.FillRectangle(BrushFor(e.Kind), rect, 3);
            if (e.IsSelected)
            {
                context.DrawRectangle(null, SelectedPen, rect, 3, 3);
                if (e.UsesDuration)
                {
                    context.FillRectangle(HandleBrush, new Rect(rect.Left + 1, rect.Top + 4, 3, rect.Height - 8));
                    context.FillRectangle(HandleBrush, new Rect(rect.Right - 4, rect.Top + 4, 3, rect.Height - 8));
                }
            }
            string label = e.Kind == CutsceneEventKind.Subtitle || string.IsNullOrWhiteSpace(e.Text) ? Short(e) : e.Text;
            using (context.PushClip(new Rect(x0, EventsY, Math.Max(width, 90), TrackHeight)))
                DrawText(context, label, new Point(x0 + 4, EventsY + 7), TextBrush, 10);
        }

        // End handle: drag to change the cutscene's length.
        double ex = X(Duration);
        context.FillRectangle(EndBrush, new Rect(ex, RulerHeight, 6, bounds.Height - RulerHeight), 2);

        // Playhead
        double px = X(_vm.Playhead);
        context.DrawLine(PlayheadPen, new Point(px, 0), new Point(px, bounds.Height));
        var head = new StreamGeometry();
        using (var g = head.Open())
        {
            g.BeginFigure(new Point(px - 6, 0), true);
            g.LineTo(new Point(px + 6, 0));
            g.LineTo(new Point(px, 8));
            g.EndFigure(true);
        }
        context.DrawGeometry(PlayheadBrush, null, head);
    }

    private static string Short(CutsceneEventItemViewModel e) => e.Kind switch
    {
        CutsceneEventKind.Subtitle => string.IsNullOrWhiteSpace(e.Text) ? "Subtitle" : "“" + e.Text + "”",
        CutsceneEventKind.FadeOut => "Fade Out",
        CutsceneEventKind.FadeIn => "Fade In",
        _ => e.Kind.ToString(),
    };

    private static IBrush BrushFor(CutsceneEventKind kind) => kind switch
    {
        CutsceneEventKind.Subtitle => new SolidColorBrush(Color.Parse("#3273EB")),
        CutsceneEventKind.FadeOut => new SolidColorBrush(Color.Parse("#555555")),
        CutsceneEventKind.FadeIn => new SolidColorBrush(Color.Parse("#777777")),
        CutsceneEventKind.Sound => new SolidColorBrush(Color.Parse("#8E6BC9")),
        _ => new SolidColorBrush(Color.Parse("#4F86B8")),
    };

    // Ruler spacing: at least ~60 px between labels.
    private static double PickStep(double duration, double width)
    {
        foreach (double s in new[] { 0.1, 0.25, 0.5, 1, 2, 5, 10, 15, 30, 60 })
            if (width / (duration / s) >= 60) return s;
        return 120;
    }

    private static void DrawText(DrawingContext context, string text, Point at, IBrush brush, double size)
    {
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, size, brush);
        context.DrawText(formatted, at);
    }

    // ------------------------------------------------------------------
    //  Input
    // ------------------------------------------------------------------

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_vm == null || !_vm.HasCutscene || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        Point p = e.GetPosition(this);
        (_mode, _dragging) = HitTest(p);
        switch (_dragging)
        {
            case CutsceneKeyItemViewModel key:
                _vm.SelectKey(key);
                _dragOffset = key.Time - TimeAt(p.X);
                break;
            case CutsceneEventItemViewModel ev:
                _vm.SelectEvent(ev);
                _dragOffset = (_mode == DragMode.EventEnd ? ev.Time + ev.Duration : ev.Time) - TimeAt(p.X);
                break;
        }
        if (_mode == DragMode.CutsceneEnd)
        {
            _dragStartX = p.X;
            _dragStartDuration = Duration;
        }
        else if (_mode == DragMode.None)
        {
            _mode = DragMode.Scrub;
            _vm.Playhead = TimeAt(p.X);
        }
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_vm == null) return;
        Point p = e.GetPosition(this);
        if (_mode == DragMode.None)
        {
            // Hover: show where an edge can be dragged.
            var (hover, _) = HitTest(p);
            Cursor = hover is DragMode.EventStart or DragMode.EventEnd or DragMode.CutsceneEnd ? ResizeCursor
                : hover is DragMode.MoveKey or DragMode.MoveEvent ? MoveCursor : Cursor.Default;
            return;
        }
        double snap = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 0.1 : 0.05;
        double Snap(double t) => Math.Round(Math.Round(t / snap) * snap, 2);
        double t = TimeAt(p.X) + _dragOffset;
        switch (_mode)
        {
            case DragMode.MoveKey when _dragging is CutsceneKeyItemViewModel key:
                key.Time = Snap(Math.Clamp(t, 0, Duration));
                break;
            case DragMode.MoveEvent when _dragging is CutsceneEventItemViewModel ev:
                ev.Time = Snap(Math.Clamp(t, 0, Duration));
                break;
            case DragMode.EventEnd when _dragging is CutsceneEventItemViewModel ev:
                // Up to the cutscene's end (drag the end handle first to go further).
                double endTime = Snap(Math.Clamp(t, ev.Time + snap, Duration));
                ev.Duration = Math.Round(endTime - ev.Time, 2);
                break;
            case DragMode.EventStart when _dragging is CutsceneEventItemViewModel ev:
                double end = ev.Time + ev.Duration;
                double start = Snap(Math.Clamp(t, 0, end - snap));
                ev.Duration = Math.Round(end - start, 2);
                ev.Time = start;
                break;
            case DragMode.CutsceneEnd:
                double length = _dragStartDuration + (p.X - _dragStartX) / TrackWidth * _dragStartDuration;
                _vm.Duration = Math.Max(0.5, Snap(length));
                break;
            case DragMode.Scrub:
                _vm.Playhead = TimeAt(p.X);
                break;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_mode == DragMode.MoveKey && _vm != null)
        {
            // Keep the list in time order once the drag ends.
            var sorted = _vm.Keys.OrderBy(k => k.Time).ToList();
            for (int i = 0; i < sorted.Count; i++)
            {
                int at = _vm.Keys.IndexOf(sorted[i]);
                if (at != i) _vm.Keys.Move(at, i);
            }
        }
        _mode = DragMode.None;
        _dragging = null;
        e.Pointer.Capture(null);
        InvalidateVisual();
    }

    private (DragMode, object?) HitTest(Point p)
    {
        if (_vm == null) return (DragMode.None, null);
        if (p.Y >= RulerHeight && Math.Abs(p.X - (X(Duration) + 3)) <= EdgeGrab) return (DragMode.CutsceneEnd, null);
        if (p.Y >= CameraY && p.Y < CameraY + TrackHeight)
        {
            var key = _vm.Keys.Where(k => Math.Abs(X(k.Time) - p.X) <= 8)
                .OrderBy(k => Math.Abs(X(k.Time) - p.X)).FirstOrDefault();
            return key != null ? (DragMode.MoveKey, key) : (DragMode.None, null);
        }
        if (p.Y >= EventsY && p.Y < EventsY + TrackHeight)
        {
            // The selected event first, so its edges win over a neighbour's body.
            foreach (var ev in _vm.Events.OrderBy(ev => ev.IsSelected ? 0 : 1).ThenByDescending(ev => _vm.Events.IndexOf(ev)))
            {
                double x0 = X(ev.Time);
                double width = ev.UsesDuration ? Math.Max(6, ev.Duration / Duration * TrackWidth) : 6;
                if (p.X < x0 - 3 || p.X > x0 + width + 3) continue;
                if (ev.UsesDuration && width >= 14)
                {
                    if (p.X >= x0 + width - EdgeGrab) return (DragMode.EventEnd, ev);
                    if (p.X <= x0 + EdgeGrab) return (DragMode.EventStart, ev);
                }
                return (DragMode.MoveEvent, ev);
            }
        }
        return (DragMode.None, null);
    }
}
