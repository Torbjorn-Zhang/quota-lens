using Avalonia;
using Avalonia.Media;
using QuotaLens.Services;

namespace QuotaLens.Mac.Rendering;

/// <summary>The menu bar image and its width in points (its height is <see cref="TrayIconRenderer.PointHeight"/>).</summary>
internal readonly record struct MenuBarImage(byte[] Png, double Width);

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
/// drawn with AppKit for the real system font; elsewhere (previews) with Avalonia. The image is only
/// as wide as its text needs, so it leaves room for other menu bar items beside the notch; it grows or
/// shrinks a little when a countdown gains or loses a digit.
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
    private const string OfflineText = "未连接";

    private static readonly Lazy<string> Font = new(() =>
    {
        using var canvas = CreateCanvas(1, 1);
        return canvas.FontDescription;
    });

    internal static double PointHeight => Height;

    /// <summary>Font the numbers are drawn in, for the self-test log.</summary>
    internal static string FontDescription => Font.Value;

    internal static MenuBarImage Render(QuotaSnapshot? snapshot, bool darkMenuBar, DateTimeOffset now)
    {
        Group codex, claude;
        using (var measure = CreateCanvas(1, 1))
        {
            codex = Layout(measure, "C", snapshot?.Codex, now);
            claude = Layout(measure, "A", snapshot?.Claude, now);
        }

        var width = Math.Ceiling(codex.Width + GroupGap + claude.Width);
        var inks = new Inks(darkMenuBar);
        using var canvas = CreateCanvas(width, Height);
        DrawGroup(canvas, 0, codex, inks);
        DrawGroup(canvas, codex.Width + GroupGap, claude, inks);
        return new MenuBarImage(canvas.EncodePng(), width);
    }

    private static IThumbnailCanvas CreateCanvas(double width, double height) => OperatingSystem.IsMacOS()
        ? new AppKitThumbnailCanvas(width, height, Scale)
        : new AvaloniaThumbnailCanvas(width, height, Scale);

    /// <summary>What one service shows, and how wide its text block is.</summary>
    private sealed record Group(string Letter, ProviderQuota? Quota, IReadOnlyList<(QuotaWindow Window, string Text)> Lines, double TextWidth)
    {
        public bool Available => Quota?.IsAvailable == true;

        public double Width => LetterWidth + LetterToRing + RingDiameter + RingToText + TextWidth;
    }

    private static Group Layout(IThumbnailCanvas measure, string letter, ProviderQuota? quota, DateTimeOffset now)
    {
        if (quota?.IsAvailable != true)
        {
            return new Group(letter, quota, Array.Empty<(QuotaWindow, string)>(),
                Math.Ceiling(measure.Measure(OfflineText, TextSize, ThumbnailFont.Text).Width));
        }

        var windows = quota.StandardWindows.Take(2).ToList();
        if (windows.Count == 0) windows = quota.Windows.Take(2).ToList();
        var lines = windows
            .Select(window => (window, $"{window.RemainingPercent:0}% {QuotaWindowLegend.CompactCountdown(window.ResetsAt, now)}"))
            .ToList();
        var widest = lines.Count == 0 ? 0 : lines.Max(line => measure.Measure(line.Item2, TextSize, ThumbnailFont.Text).Width);
        return new Group(letter, quota, lines, DotDiameter + DotToText + Math.Ceiling(widest));
    }

    private static void DrawGroup(IThumbnailCanvas canvas, double x, Group group, Inks inks)
    {
        var letterSize = canvas.Measure(group.Letter, LetterSize, ThumbnailFont.Letter);
        canvas.DrawText(group.Letter, x + (LetterWidth - letterSize.Width) / 2, (Height - letterSize.Height) / 2,
            LetterSize, ThumbnailFont.Letter, group.Available ? inks.Label : inks.Muted);

        var ringLeft = x + LetterWidth + LetterToRing;
        DrawRings(canvas, new Point(ringLeft + RingDiameter / 2, Height / 2),
            group.Available ? group.Quota!.Windows.Take(RingPainter.MaxRings).ToList() : Array.Empty<QuotaWindow>(), inks);

        var dotLeft = ringLeft + RingDiameter + RingToText;
        if (!group.Available)
        {
            var offline = canvas.Measure(OfflineText, TextSize, ThumbnailFont.Text);
            canvas.DrawText(OfflineText, dotLeft, (Height - offline.Height) / 2, TextSize, ThumbnailFont.Text, inks.Muted);
            return;
        }

        var textLeft = dotLeft + DotDiameter + DotToText;
        var lineHeight = Height / 2;
        for (var index = 0; index < group.Lines.Count; index++)
        {
            var (window, text) = group.Lines[index];
            var size = canvas.Measure(text, TextSize, ThumbnailFont.Text);
            var middle = group.Lines.Count == 1 ? Height / 2 : (index + 0.5) * lineHeight;
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
