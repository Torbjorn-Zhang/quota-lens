using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace QuotaLens.Mac.Rendering;

/// <summary>
/// Draws the menu bar icon: "C" with Codex's rings, then "A" with Claude's rings. macOS scales the
/// image to the menu bar height and keeps its aspect ratio, so it is rendered at twice the ~18 pt
/// menu bar height for Retina sharpness. The icon is coloured (not a template image), so letters and
/// tracks switch between light and dark to stay visible on either menu bar appearance.
/// </summary>
internal static class TrayIconRenderer
{
    private const double Height = 18;
    private const double RingDiameter = 17;
    private const double RingStroke = 1.9;
    private const double RingGap = 0.6;
    private const double LetterWidth = 7.5;
    private const double LetterToRing = 1.5;
    private const double GroupGap = 5;
    private const double Scale = 2;

    /// <summary>Display size of the icon in points; the PNG is rendered at twice this.</summary>
    internal static double PointWidth => Math.Ceiling(2 * (LetterWidth + LetterToRing + RingDiameter) + GroupGap);
    internal static double PointHeight => Height;

    internal static byte[] RenderPng(QuotaSnapshot? snapshot, bool darkMenuBar)
    {
        using var bitmap = new RenderTargetBitmap(
            new PixelSize((int)(PointWidth * Scale), (int)(PointHeight * Scale)),
            new Vector(96 * Scale, 96 * Scale));

        var letterBrush = QuotaPalette.Hex(darkMenuBar ? "#EAF0FF" : "#1C2230");
        var track = QuotaPalette.Hex(darkMenuBar ? "#38FFFFFF" : "#30000000");
        using (var context = bitmap.CreateDrawingContext())
        using (context.PushRenderOptions(new RenderOptions { TextRenderingMode = TextRenderingMode.Antialias }))
        {
            // Greyscale text smoothing: sub-pixel colour fringes look wrong once macOS rescales the icon.
            var x = 0.0;
            x = DrawGroup(context, x, "C", snapshot?.Codex, letterBrush, track);
            DrawGroup(context, x + GroupGap, "A", snapshot?.Claude, letterBrush, track);
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream);
        return stream.ToArray();
    }

    private static double DrawGroup(
        DrawingContext context,
        double x,
        string letter,
        ProviderQuota? quota,
        IBrush letterBrush,
        IBrush track)
    {
        var available = quota?.IsAvailable == true;
        var text = new FormattedText(
            letter,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Helvetica Neue, Segoe UI, Arial", FontStyle.Normal, FontWeight.Bold),
            11,
            available ? letterBrush : QuotaPalette.Hex("#808A99"));
        context.DrawText(text, new Point(x + (LetterWidth - text.Width) / 2, (Height - text.Height) / 2));

        var ringLeft = x + LetterWidth + LetterToRing;
        var bounds = new Rect(ringLeft, (Height - RingDiameter) / 2, RingDiameter, RingDiameter);
        RingPainter.Draw(
            context,
            bounds,
            available ? quota!.Windows : Array.Empty<QuotaWindow>(),
            RingStroke,
            RingGap,
            track);
        return ringLeft + RingDiameter;
    }
}
