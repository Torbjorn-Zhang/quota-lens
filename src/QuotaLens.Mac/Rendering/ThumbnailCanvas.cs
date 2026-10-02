using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;

namespace QuotaLens.Mac.Rendering;

internal enum ThumbnailFont
{
    /// <summary>The service letters, semibold.</summary>
    Letter,

    /// <summary>Percentages and countdowns, medium weight with equal-width digits where available.</summary>
    Text
}

/// <summary>
/// The few drawing operations the menu bar thumbnail needs, in points with a top-left origin, so the
/// same layout can be drawn with AppKit on macOS (the real system font) and with Avalonia elsewhere
/// (previews on other machines).
/// </summary>
internal interface IThumbnailCanvas : IDisposable
{
    /// <summary>Font actually used for text, for the self-test log.</summary>
    string FontDescription { get; }

    Size Measure(string text, double size, ThumbnailFont font);

    void DrawText(string text, double left, double top, double size, ThumbnailFont font, Color colour);

    void StrokeCircle(Point centre, double radius, double width, Color colour);

    void FillCircle(Point centre, double radius, Color colour);

    /// <summary>Clockwise arc from twelve o'clock covering <paramref name="fraction"/> of the circle, round caps.</summary>
    void StrokeArc(Point centre, double radius, double fraction, double width, Color colour);

    byte[] EncodePng();
}

/// <summary>Avalonia implementation, used where AppKit is not available.</summary>
internal sealed class AvaloniaThumbnailCanvas : IThumbnailCanvas
{
    private static readonly FontFamily Family = new("Helvetica Neue, Segoe UI, Arial");
    private static readonly Typeface LetterFace = new(Family, FontStyle.Normal, FontWeight.SemiBold);
    private static readonly Typeface TextFace = new(Family, FontStyle.Normal, FontWeight.Medium);

    private readonly RenderTargetBitmap _bitmap;
    private DrawingContext? _context;
    private IDisposable? _options;

    public AvaloniaThumbnailCanvas(double widthPoints, double heightPoints, double scale)
    {
        _bitmap = new RenderTargetBitmap(
            new PixelSize((int)Math.Round(widthPoints * scale), (int)Math.Round(heightPoints * scale)),
            new Vector(96 * scale, 96 * scale));
        _context = _bitmap.CreateDrawingContext();
        // Greyscale text smoothing: sub-pixel colour fringes look wrong once macOS composites the icon.
        _options = _context.PushRenderOptions(new RenderOptions { TextRenderingMode = TextRenderingMode.Antialias });
    }

    public string FontDescription =>
        FontManager.Current.TryGetGlyphTypeface(TextFace, out var glyphs) ? glyphs.FamilyName : "?";

    public Size Measure(string text, double size, ThumbnailFont font)
    {
        var formatted = Format(text, size, font, Colors.Black);
        return new Size(formatted.Width, formatted.Height);
    }

    public void DrawText(string text, double left, double top, double size, ThumbnailFont font, Color colour) =>
        _context?.DrawText(Format(text, size, font, colour), new Point(left, top));

    public void StrokeCircle(Point centre, double radius, double width, Color colour) =>
        _context?.DrawEllipse(null, new Pen(new ImmutableSolidColorBrush(colour), width), centre, radius, radius);

    public void FillCircle(Point centre, double radius, Color colour) =>
        _context?.DrawEllipse(new ImmutableSolidColorBrush(colour), null, centre, radius, radius);

    public void StrokeArc(Point centre, double radius, double fraction, double width, Color colour) =>
        _context?.DrawGeometry(
            null,
            new Pen(new ImmutableSolidColorBrush(colour), width, lineCap: PenLineCap.Round),
            RingPainter.Arc(centre, radius, fraction));

    public byte[] EncodePng()
    {
        Finish();
        using var stream = new MemoryStream();
        _bitmap.Save(stream);
        return stream.ToArray();
    }

    public void Dispose()
    {
        Finish();
        _bitmap.Dispose();
    }

    private void Finish()
    {
        _options?.Dispose();
        _options = null;
        _context?.Dispose();
        _context = null;
    }

    private static FormattedText Format(string text, double size, ThumbnailFont font, Color colour) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            font == ThumbnailFont.Letter ? LetterFace : TextFace, size, new ImmutableSolidColorBrush(colour));
}
