using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace QuotaLens.Mac.Rendering;

/// <summary>
/// The 1024 px application icon (Finder, Login Items, the Keychain prompt): the three quota rings
/// on the widget's dark glass, inside the standard macOS icon grid (824 px tile, 100 px margin).
/// Packaging/AppIcon.png is generated from this with <c>--render-preview</c>.
/// </summary>
internal static class AppIconRenderer
{
    internal static byte[] RenderPng()
    {
        const int size = 1024;
        using var bitmap = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));
        using (var context = bitmap.CreateDrawingContext())
        {
            var tile = new Rect(100, 100, 824, 824);
            var background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.Parse("#1D2743"), 0),
                    new GradientStop(Color.Parse("#0C1222"), 1)
                }
            };
            context.DrawRectangle(
                background,
                new Pen(QuotaPalette.Hex("#40FFFFFF"), 6),
                new RoundedRect(tile, 185));

            var sample = new[]
            {
                new QuotaWindow("5 小时", 15, null),
                new QuotaWindow("7 天", 40, null),
                new QuotaWindow("Fable 周额度", 65, null, IsModelScoped: true)
            };
            var rings = tile.Deflate(150);
            RingPainter.Draw(context, rings, sample, 58, 22, QuotaPalette.Hex("#26FFFFFF"));
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream);
        return stream.ToArray();
    }
}
