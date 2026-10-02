using Avalonia.Media;
using Avalonia.Media.Immutable;
using QuotaLens.Services;

namespace QuotaLens.Mac.Rendering;

/// <summary>Avalonia brushes for the palette defined once in Core and shared with the Windows app.</summary>
internal static class QuotaPalette
{
    private static readonly Dictionary<string, IImmutableSolidColorBrush> Cache = new(StringComparer.OrdinalIgnoreCase);

    internal static IImmutableSolidColorBrush Hex(string hex)
    {
        lock (Cache)
        {
            if (!Cache.TryGetValue(hex, out var brush))
            {
                brush = new ImmutableSolidColorBrush(Color.Parse(hex));
                Cache[hex] = brush;
            }
            return brush;
        }
    }

    internal static IImmutableSolidColorBrush Identity(QuotaWindow window) => Hex(QuotaWindowLegend.IdentityHex(window));

    internal static IImmutableSolidColorBrush Bar(QuotaWindow window, bool onLight = false) =>
        Hex(Shade(QuotaWindowLegend.BarHex(window), onLight));

    /// <summary>The palette colour itself on dark surfaces, its deeper counterpart on light ones.</summary>
    internal static string Shade(string hex, bool onLight) => onLight ? QuotaWindowLegend.OnLightHex(hex) : hex;

    internal static IImmutableSolidColorBrush Level(double remaining, IImmutableSolidColorBrush normal) =>
        QuotaWindowLegend.LevelHex(remaining) is string hex ? Hex(hex) : normal;

    internal static IImmutableSolidColorBrush Critical => Hex(QuotaWindowLegend.CriticalHex);
    internal static IImmutableSolidColorBrush Text => Hex("#F3F7FF");
    internal static IImmutableSolidColorBrush Muted => Hex("#9DB2C8");
}
