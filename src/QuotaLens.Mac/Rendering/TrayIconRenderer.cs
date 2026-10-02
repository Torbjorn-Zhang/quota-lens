using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using QuotaLens.Services;

namespace QuotaLens.Mac.Rendering;

/// <summary>
/// Draws the menu bar thumbnail, the macOS counterpart of the Windows sidebar strip. Per service:
/// its letter, its concentric rings, and two compact lines, the 5-hour window then the 7-day window,
/// each as "remaining% countdown" (for example "77% 2h01m" and "73% 2d14h"). The model allowance
/// stays in the innermost ring; the panel has all details. Rendered at 2× for Retina.
/// </summary>
/// <remarks>
/// Each text block reserves the width of its widest possible value, so the menu bar item keeps a
/// constant width as the countdown ticks and never nudges the neighbouring icons. The image is
/// coloured, not a template, and the macOS 26 menu bar is transparent, so the background stays clear
/// and every glyph and ring arc gets a thin outline instead: dark glass around the bright palette on
/// dark bars, white around the palette's deeper light-surface shades on light bars. The outline keeps
/// the colours readable on bright or busy wallpapers without a solid plate.
/// </remarks>
internal static class TrayIconRenderer
{
    private const double Height = 22;
    private const double EdgePadding = 1.5;
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

    // Outline pens are centred on the glyph and arc edges, so about half of each width shows outside.
    private const double TextOutline = 1.6;
    private const double RingOutline = RingGap * 2;
    private const string DarkOutline = "#E00F1526";
    private const string LightOutline = "#F0FFFFFF";

    private static readonly Typeface LetterFace =
        new("Helvetica Neue, Segoe UI, Arial", FontStyle.Normal, FontWeight.Bold);
    private static readonly Typeface TextFace =
        new("Helvetica Neue, Segoe UI, Arial", FontStyle.Normal, FontWeight.SemiBold);

    /// <summary>Display size of the thumbnail in points; the PNG is rendered at twice this.</summary>
    internal static double PointWidth => Math.Ceiling(2 * GroupWidth + GroupGap + 2 * EdgePadding);
    internal static double PointHeight => Height;

    private static double TextBlockWidth => Math.Ceiling(Measure(WidestLine, TextFace, TextSize, Brushes.White).Width) + 1;
    private static double GroupWidth => LetterWidth + LetterToRing + RingDiameter + RingToText + TextBlockWidth;

    internal static byte[] RenderPng(QuotaSnapshot? snapshot, bool darkMenuBar, DateTimeOffset now)
    {
        using var bitmap = new RenderTargetBitmap(
            new PixelSize((int)(PointWidth * Scale), (int)(PointHeight * Scale)),
            new Vector(96 * Scale, 96 * Scale));

        using (var context = bitmap.CreateDrawingContext())
        using (context.PushRenderOptions(new RenderOptions { TextRenderingMode = TextRenderingMode.Antialias }))
        {
            // Greyscale text smoothing: sub-pixel colour fringes look wrong once macOS composites the icon.
            DrawGroup(context, EdgePadding, "C", snapshot?.Codex, darkMenuBar, now);
            DrawGroup(context, EdgePadding + GroupWidth + GroupGap, "A", snapshot?.Claude, darkMenuBar, now);
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
        bool dark,
        DateTimeOffset now)
    {
        var available = quota?.IsAvailable == true;
        var muted = QuotaPalette.Hex(dark ? "#9DB2C8" : "#5F6878");
        var outline = QuotaPalette.Hex(dark ? DarkOutline : LightOutline);

        var letterText = Measure(letter, LetterFace, 11, available ? QuotaPalette.Hex(dark ? "#EAF0FF" : "#1C2230") : muted);
        DrawOutlined(context, letterText, new Point(x + (LetterWidth - letterText.Width) / 2, (Height - letterText.Height) / 2), outline);

        var ringLeft = x + LetterWidth + LetterToRing;
        RingPainter.Draw(
            context,
            new Rect(ringLeft, (Height - RingDiameter) / 2, RingDiameter, RingDiameter),
            available ? quota!.Windows : Array.Empty<QuotaWindow>(),
            RingStroke,
            RingGap,
            QuotaPalette.Hex(dark ? "#38FFFFFF" : "#30000000"),
            onLight: !dark,
            outline: outline,
            outlineWidth: RingOutline);

        var textLeft = ringLeft + RingDiameter + RingToText;
        if (!available)
        {
            var offline = Measure("未连接", TextFace, TextSize, muted);
            DrawOutlined(context, offline, new Point(textLeft, (Height - offline.Height) / 2), outline);
            return;
        }

        var lines = quota!.StandardWindows.Take(2).ToList();
        if (lines.Count == 0) lines = quota.Windows.Take(2).ToList();
        var lineHeight = Height / 2;
        for (var index = 0; index < lines.Count; index++)
        {
            var window = lines[index];
            var text = $"{window.RemainingPercent:0}% {QuotaWindowLegend.CompactCountdown(window.ResetsAt, now)}";
            var formatted = Measure(text, TextFace, TextSize, LineBrush(window, dark));
            var top = lines.Count == 1
                ? (Height - formatted.Height) / 2
                : index * lineHeight + (lineHeight - formatted.Height) / 2;
            DrawOutlined(context, formatted, new Point(textLeft, top), outline);
        }
    }

    /// <summary>Strokes the glyph outlines first, then draws the text over them.</summary>
    private static void DrawOutlined(DrawingContext context, FormattedText text, Point origin, IBrush outline)
    {
        if (text.BuildGeometry(origin) is Geometry glyphs)
        {
            context.DrawGeometry(null, new Pen(outline, TextOutline, lineJoin: PenLineJoin.Round), glyphs);
        }
        context.DrawText(text, origin);
    }

    /// <summary>Identity colour of the window (warning colour when low), deepened for light menu bars.</summary>
    private static IBrush LineBrush(QuotaWindow window, bool dark)
    {
        var hex = QuotaWindowLegend.LevelHex(window.RemainingPercent) ?? QuotaWindowLegend.IdentityHex(window);
        return QuotaPalette.Hex(QuotaPalette.Shade(hex, onLight: !dark));
    }

    private static FormattedText Measure(string text, Typeface face, double size, IBrush brush) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, size, brush);
}
