using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace QuotaLens.Mac.Rendering;

/// <summary>
/// Concentric quota rings, one per window from the outside in (5-hour, 7-day, model allowance, at
/// most three): a faint full track plus a clockwise arc from twelve o'clock covering what remains,
/// in the window's identity colour (red at 20% or less), deepened on light surfaces. Same geometry
/// as the Windows sidebar.
/// </summary>
internal static class RingPainter
{
    internal const int MaxRings = 3;

    internal static void Draw(
        DrawingContext context,
        Rect bounds,
        IReadOnlyList<QuotaWindow> windows,
        double stroke,
        double gap,
        IBrush track,
        bool onLight = false)
    {
        var size = Math.Min(bounds.Width, bounds.Height);
        var center = bounds.Center;
        var rings = Math.Max(1, Math.Min(MaxRings, windows.Count));
        var trackPen = new Pen(track, stroke);

        for (var index = 0; index < rings; index++)
        {
            var radius = size / 2 - stroke / 2 - index * (stroke + gap);
            if (radius <= stroke / 2) break;

            context.DrawEllipse(null, trackPen, center, radius, radius);
            if (index >= windows.Count) continue;

            var window = windows[index];
            var fraction = Math.Clamp(window.RemainingPercent, 0, 100) / 100;
            if (fraction <= 0.005) continue;

            var pen = new Pen(QuotaPalette.Bar(window, onLight), stroke, lineCap: PenLineCap.Round);
            if (fraction >= 0.999)
            {
                context.DrawEllipse(null, pen, center, radius, radius);
                continue;
            }

            var angle = fraction * 2 * Math.PI;
            var start = new Point(center.X, center.Y - radius);
            var end = new Point(center.X + radius * Math.Sin(angle), center.Y - radius * Math.Cos(angle));
            var geometry = new StreamGeometry();
            using (var figure = geometry.Open())
            {
                figure.BeginFigure(start, isFilled: false);
                figure.ArcTo(end, new Size(radius, radius), 0, fraction > 0.5, SweepDirection.Clockwise);
                figure.EndFigure(isClosed: false);
            }
            context.DrawGeometry(null, pen, geometry);
        }
    }
}

/// <summary>Panel control wrapping <see cref="RingPainter"/>.</summary>
internal sealed class RingGauge : Control
{
    private IReadOnlyList<QuotaWindow> _windows = Array.Empty<QuotaWindow>();

    public double Stroke { get; set; } = 5;
    public double Gap { get; set; } = 2.5;

    public IReadOnlyList<QuotaWindow> Windows
    {
        get => _windows;
        set
        {
            _windows = value;
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context) =>
        RingPainter.Draw(context, new Rect(Bounds.Size), _windows, Stroke, Gap, QuotaPalette.Hex("#26FFFFFF"));
}
