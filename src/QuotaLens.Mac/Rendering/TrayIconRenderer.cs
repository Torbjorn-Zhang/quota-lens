using Avalonia;
using Avalonia.Media;
using QuotaLens.Services;

namespace QuotaLens.Mac.Rendering;

/// <summary>
/// Draws the menu bar thumbnail, the macOS counterpart of the Windows sidebar strip. Per service:
/// its letter, its concentric rings, and two compact lines, the 5-hour window then the 7-day window,
/// each as "remaining% countdown" (for example "77% 2h01m" and "73% 2d14h"). The model allowance
/// stays in the innermost ring; the panel has all details. Rendered at 2× for Retina.
/// </summary>
/// <remarks>
/// Styled after Apple's Activity rings and menu bar: each ring is drawn in its window's identity
/// colour over a track of the same colour at low opacity, and each line starts with a dot in that
/// colour, so the colours say which line belongs to which ring. Text is in the menu bar's label
/// colour (white on dark, black on light) in the system font with equal-width digits like the menu
/// bar clock, which keeps it as readable as the system's own items on any wallpaper; only a low
/// percentage turns Apple's increased-contrast orange (40% or less) or red (20% or less), and a ring
/// at 20% or less turns red. Light menu bars get deeper shades of the identity colours. On macOS it is
/// drawn with AppKit for the real system font; elsewhere (previews) with Avalonia. Each text block
/// reserves the width of its widest possible value, so the item keeps a constant width.
/// </remarks>
internal static class TrayIconRenderer
{
    private const double Height = 22;
    private const double RingDiameter = 17;
    private const double RingStroke = 1.9;
    private const double RingGap = 0.6;
    private const double LetterWidth = 7;
    private const double LetterToRing = 1;
    private const double RingToText = 2.5;
    private const double DotDiameter = 4.5;
    private const double DotToText = 2;
    private const double GroupGap = 5;
    private const double LetterSize = 11;
    private const double TextSize = 9;
    private const double Scale = 2;
    private const string WidestLine = "100% 23h59m";

    private static readonly Lazy<(double TextWidth, string Font)> Metrics = new(() =>
    {
        using var canvas = CreateCanvas(1, 1);
        return (Math.Ceiling(canvas.Measure(WidestLine, TextSize, ThumbnailFont.Text).Width) + 1, canvas.FontDescription);
    });

    /// <summary>Display size of the thumbnail in points; the PNG is rendered at twice this.</summary>
    internal static double PointWidth => Math.Ceiling(2 * GroupWidth + GroupGap);
    internal static double PointHeight => Height;

    /// <summary>Font the numbers are drawn in, for the self-test log.</summary>
    internal static string FontDescription => Metrics.Value.Font;

    private static double GroupWidth =>
        LetterWidth + LetterToRing + RingDiameter + RingToText + DotDiameter + DotToText + Metrics.Value.TextWidth;

    internal static byte[] RenderPng(QuotaSnapshot? snapshot, bool darkMenuBar, DateTimeOffset now)
    {
        var inks = new Inks(darkMenuBar);
        using var canvas = CreateCanvas(PointWidth, PointHeight);
        DrawGroup(canvas, 0, "C", snapshot?.Codex, inks, now);
        DrawGroup(canvas, GroupWidth + GroupGap, "A", snapshot?.Claude, inks, now);
        return canvas.EncodePng();
    }

    private static IThumbnailCanvas CreateCanvas(double width, double height) => OperatingSystem.IsMacOS()
        ? new AppKitThumbnailCanvas(width, height, Scale)
        : new AvaloniaThumbnailCanvas(width, height, Scale);

    private static void DrawGroup(
        IThumbnailCanvas canvas,
        double x,
        string letter,
        ProviderQuota? quota,
        Inks inks,
        DateTimeOffset now)
    {
        var available = quota?.IsAvailable == true;

        var letterSize = canvas.Measure(letter, LetterSize, ThumbnailFont.Letter);
        canvas.DrawText(letter, x + (LetterWidth - letterSize.Width) / 2, (Height - letterSize.Height) / 2,
            LetterSize, ThumbnailFont.Letter, available ? inks.Label : inks.Muted);

        var ringLeft = x + LetterWidth + LetterToRing;
        DrawRings(canvas, new Point(ringLeft + RingDiameter / 2, Height / 2),
            available ? quota!.Windows.Take(RingPainter.MaxRings).ToList() : Array.Empty<QuotaWindow>(), inks);

        var dotLeft = ringLeft + RingDiameter + RingToText;
        var textLeft = dotLeft + DotDiameter + DotToText;
        if (!available)
        {
            var offline = canvas.Measure("未连接", TextSize, ThumbnailFont.Text);
            canvas.DrawText("未连接", dotLeft, (Height - offline.Height) / 2, TextSize, ThumbnailFont.Text, inks.Muted);
            return;
        }

        var lines = quota!.StandardWindows.Take(2).ToList();
        if (lines.Count == 0) lines = quota.Windows.Take(2).ToList();
        var lineHeight = Height / 2;
        for (var index = 0; index < lines.Count; index++)
        {
            var window = lines[index];
            var text = $"{window.RemainingPercent:0}% {QuotaWindowLegend.CompactCountdown(window.ResetsAt, now)}";
            var size = canvas.Measure(text, TextSize, ThumbnailFont.Text);
            var middle = lines.Count == 1 ? Height / 2 : (index + 0.5) * lineHeight;
            canvas.FillCircle(new Point(dotLeft + DotDiameter / 2, middle), DotDiameter / 2, inks.Identity(window));
            canvas.DrawText(text, textLeft, middle - size.Height / 2, TextSize, ThumbnailFont.Text,
                inks.Level(window.RemainingPercent));
        }
    }

    /// <summary>Same geometry as <see cref="RingPainter"/>: outer 5-hour, middle 7-day, inner model.</summary>
    private static void DrawRings(IThumbnailCanvas canvas, Point centre, IReadOnlyList<QuotaWindow> windows, Inks inks)
    {
        var rings = Math.Max(1, Math.Min(RingPainter.MaxRings, windows.Count));
        for (var index = 0; index < rings; index++)
        {
            var radius = RingDiameter / 2 - RingStroke / 2 - index * (RingStroke + RingGap);
            if (index >= windows.Count)
            {
                canvas.StrokeCircle(centre, radius, RingStroke, inks.EmptyTrack);
                continue;
            }

            var window = windows[index];
            canvas.StrokeCircle(centre, radius, RingStroke, inks.Track(window));
            var fraction = Math.Clamp(window.RemainingPercent, 0, 100) / 100;
            if (fraction > 0.005) canvas.StrokeArc(centre, radius, fraction, RingStroke, inks.Arc(window));
        }
    }

    /// <summary>The colours of one appearance.</summary>
    private readonly struct Inks
    {
        private readonly bool _dark;

        public Inks(bool dark) => _dark = dark;

        // Approximations of the menu bar's label colours.
        public Color Label => Color.Parse(_dark ? "#F2FFFFFF" : "#D9000000");
        public Color Muted => Color.Parse(_dark ? "#8CFFFFFF" : "#80000000");
        public Color EmptyTrack => Color.Parse(_dark ? "#40FFFFFF" : "#2E000000");

        public Color Identity(QuotaWindow window) => Color.Parse(Shade(QuotaWindowLegend.IdentityHex(window)));

        /// <summary>The identity colour at low opacity, like an Activity ring's track.</summary>
        public Color Track(QuotaWindow window)
        {
            var colour = Identity(window);
            return new Color((byte)(_dark ? 0x4D : 0x40), colour.R, colour.G, colour.B);
        }

        public Color Arc(QuotaWindow window) =>
            QuotaWindowLegend.BarHex(window) == QuotaWindowLegend.CriticalHex ? Alert(QuotaWindowLegend.CriticalHex) : Identity(window);

        /// <summary>Label colour, or the alert colour when the percentage is low.</summary>
        public Color Level(double remaining) =>
            QuotaWindowLegend.LevelHex(remaining) is string hex ? Alert(hex) : Label;

        // Apple's increased-contrast systemRed and systemOrange.
        private Color Alert(string hex) => Color.Parse(hex == QuotaWindowLegend.CriticalHex
            ? _dark ? "#FF6165" : "#E9152D"
            : _dark ? "#FFA056" : "#C55300");

        /// <summary>The palette colour on dark menu bars; a deeper shade of it on light ones.</summary>
        private string Shade(string hex) => _dark ? hex : hex switch
        {
            QuotaWindowLegend.SessionHex => "#0E2FFD",
            QuotaWindowLegend.WeeklyHex => "#A211AC",
            QuotaWindowLegend.ModelHex => "#1D8071",
            _ => "#4E5A6E"
        };
    }
}
