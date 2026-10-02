using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using QuotaLens.Services;

namespace QuotaLens.Mac.Rendering;

/// <summary>The menu bar image and whether macOS should treat it as a template.</summary>
internal readonly record struct MenuBarImage(byte[] Png, bool IsTemplate);

/// <summary>
/// Draws the menu bar thumbnail, the macOS counterpart of the Windows sidebar strip. Per service:
/// its letter, its concentric rings, and two compact lines, the 5-hour window then the 7-day window,
/// each as "remaining% countdown" (for example "77% 2h01m" and "73% 2d14h"). The model allowance
/// stays in the innermost ring; the panel has all details. Rendered at 2× for Retina.
/// </summary>
/// <remarks>
/// It follows Apple's own menu bar items: normally a monochrome template image in the system font,
/// which macOS tints and treats exactly like the clock or the battery, so it stays legible on any
/// wallpaper behind the transparent menu bar and in either appearance. Colour only appears as an
/// alert, the way the battery turns red: a percentage at 40% or less turns orange, at 20% or less
/// red, and a ring at 20% or less turns red, in Apple's increased-contrast system colours. An image
/// with colour cannot be a template, so while an alert shows it is drawn in the menu bar's label
/// colour for the current appearance. Each text block reserves the width of its widest possible
/// value, so the item keeps a constant width as the countdown ticks.
/// </remarks>
internal static class TrayIconRenderer
{
    private const double Height = 22;
    private const double RingDiameter = 17;
    private const double RingStroke = 1.9;
    private const double RingGap = 0.6;
    private const double LetterWidth = 7.5;
    private const double LetterToRing = 1.5;
    private const double RingToText = 3;
    private const double GroupGap = 7;
    private const double TextSize = 9;
    private const double Scale = 2;
    private const string WidestLine = "100% 23h59m";

    private static readonly FontFamily SystemFont = new(".AppleSystemUIFont, Helvetica Neue, Segoe UI, Arial");
    private static readonly Typeface LetterFace = new(SystemFont, FontStyle.Normal, FontWeight.SemiBold);
    private static readonly Typeface TextFace = new(SystemFont, FontStyle.Normal, FontWeight.Medium);

    /// <summary>Display size of the thumbnail in points; the PNG is rendered at twice this.</summary>
    internal static double PointWidth => Math.Ceiling(2 * GroupWidth + GroupGap);
    internal static double PointHeight => Height;

    /// <summary>Family the system font resolved to, for the self-test log.</summary>
    internal static string ResolvedFontFamily =>
        FontManager.Current.TryGetGlyphTypeface(TextFace, out var glyphs) ? glyphs.FamilyName : "?";

    private static double TextBlockWidth => Math.Ceiling(Measure(WidestLine, TextFace, TextSize, Brushes.White).Width) + 1;
    private static double GroupWidth => LetterWidth + LetterToRing + RingDiameter + RingToText + TextBlockWidth;

    /// <summary>The status item image: a template, unless a shown window needs an alert colour.</summary>
    internal static MenuBarImage Render(QuotaSnapshot? snapshot, bool darkMenuBar, DateTimeOffset now)
    {
        var alert = NeedsAlert(snapshot?.Codex) || NeedsAlert(snapshot?.Claude);
        return new MenuBarImage(Draw(snapshot, new Inks(alert ? Mode(darkMenuBar) : InkMode.Template), now), !alert);
    }

    /// <summary>How the menu bar shows the image, in its label colour, for previews and the self-test.</summary>
    internal static byte[] RenderPreviewPng(QuotaSnapshot? snapshot, bool darkMenuBar, DateTimeOffset now) =>
        Draw(snapshot, new Inks(Mode(darkMenuBar)), now);

    private static InkMode Mode(bool dark) => dark ? InkMode.Dark : InkMode.Light;

    private static byte[] Draw(QuotaSnapshot? snapshot, Inks inks, DateTimeOffset now)
    {
        using var bitmap = new RenderTargetBitmap(
            new PixelSize((int)(PointWidth * Scale), (int)(PointHeight * Scale)),
            new Vector(96 * Scale, 96 * Scale));

        using (var context = bitmap.CreateDrawingContext())
        using (context.PushRenderOptions(new RenderOptions { TextRenderingMode = TextRenderingMode.Antialias }))
        {
            // Greyscale text smoothing: sub-pixel colour fringes look wrong once macOS composites the icon.
            DrawGroup(context, 0, "C", snapshot?.Codex, inks, now);
            DrawGroup(context, GroupWidth + GroupGap, "A", snapshot?.Claude, inks, now);
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream);
        return stream.ToArray();
    }

    private static void DrawGroup(
        DrawingContext context,
        double x,
        string letter,
        ProviderQuota? quota,
        Inks inks,
        DateTimeOffset now)
    {
        var available = quota?.IsAvailable == true;

        var letterText = Measure(letter, LetterFace, 11, available ? inks.Label : inks.Muted);
        context.DrawText(letterText, new Point(x + (LetterWidth - letterText.Width) / 2, (Height - letterText.Height) / 2));

        var ringLeft = x + LetterWidth + LetterToRing;
        RingPainter.Draw(
            context,
            new Rect(ringLeft, (Height - RingDiameter) / 2, RingDiameter, RingDiameter),
            available ? RingWindows(quota!) : Array.Empty<QuotaWindow>(),
            RingStroke,
            RingGap,
            inks.Track,
            window => inks.Alert(QuotaWindowLegend.BarHex(window) == QuotaWindowLegend.CriticalHex ? QuotaWindowLegend.CriticalHex : null));

        var textLeft = ringLeft + RingDiameter + RingToText;
        if (!available)
        {
            var offline = Measure("未连接", TextFace, TextSize, inks.Muted);
            context.DrawText(offline, new Point(textLeft, (Height - offline.Height) / 2));
            return;
        }

        var lines = TextWindows(quota!);
        var lineHeight = Height / 2;
        for (var index = 0; index < lines.Count; index++)
        {
            var window = lines[index];
            var text = $"{window.RemainingPercent:0}% {QuotaWindowLegend.CompactCountdown(window.ResetsAt, now)}";
            var formatted = Measure(text, TextFace, TextSize, inks.Alert(QuotaWindowLegend.LevelHex(window.RemainingPercent)));
            var top = lines.Count == 1
                ? (Height - formatted.Height) / 2
                : index * lineHeight + (lineHeight - formatted.Height) / 2;
            context.DrawText(formatted, new Point(textLeft, top));
        }
    }

    private static bool NeedsAlert(ProviderQuota? quota) =>
        quota?.IsAvailable == true && (
            TextWindows(quota).Any(window => QuotaWindowLegend.LevelHex(window.RemainingPercent) is not null) ||
            RingWindows(quota).Any(window => QuotaWindowLegend.BarHex(window) == QuotaWindowLegend.CriticalHex));

    private static IReadOnlyList<QuotaWindow> RingWindows(ProviderQuota quota) =>
        quota.Windows.Take(RingPainter.MaxRings).ToList();

    private static IReadOnlyList<QuotaWindow> TextWindows(ProviderQuota quota)
    {
        var lines = quota.StandardWindows.Take(2).ToList();
        return lines.Count > 0 ? lines : quota.Windows.Take(2).ToList();
    }

    private static FormattedText Measure(string text, Typeface face, double size, IBrush brush) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, size, brush);

    private enum InkMode
    {
        /// <summary>Black with alpha only; macOS supplies the colour.</summary>
        Template,
        Dark,
        Light
    }

    /// <summary>The few colours a thumbnail uses in one mode.</summary>
    private readonly struct Inks
    {
        private readonly InkMode _mode;

        public Inks(InkMode mode) => _mode = mode;

        // Approximations of the menu bar's label colour; macOS uses the real one for templates.
        public IBrush Label => QuotaPalette.Hex(_mode switch { InkMode.Dark => "#F2FFFFFF", InkMode.Light => "#D9000000", _ => "#FF000000" });
        public IBrush Muted => QuotaPalette.Hex(_mode switch { InkMode.Dark => "#8CFFFFFF", InkMode.Light => "#80000000", _ => "#80000000" });
        public IBrush Track => QuotaPalette.Hex(_mode switch { InkMode.Dark => "#40FFFFFF", InkMode.Light => "#2E000000", _ => "#40000000" });

        /// <summary>The label colour, or the alert colour for a warning or critical palette colour.</summary>
        public IBrush Alert(string? levelHex) => levelHex switch
        {
            // Apple's increased-contrast systemRed and systemOrange.
            QuotaWindowLegend.CriticalHex when _mode == InkMode.Dark => QuotaPalette.Hex("#FF6165"),
            QuotaWindowLegend.CriticalHex when _mode == InkMode.Light => QuotaPalette.Hex("#E9152D"),
            QuotaWindowLegend.WarningHex when _mode == InkMode.Dark => QuotaPalette.Hex("#FFA056"),
            QuotaWindowLegend.WarningHex when _mode == InkMode.Light => QuotaPalette.Hex("#C55300"),
            _ => Label
        };
    }
}
