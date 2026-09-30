namespace QuotaLens.Services;

/// <summary>Kind of quota window; each kind has a fixed identity colour in the UI.</summary>
internal enum QuotaWindowKind
{
    /// <summary>The rolling 5-hour session allowance.</summary>
    Session,

    /// <summary>The 7-day allowance across all models.</summary>
    Weekly,

    /// <summary>A model-family weekly allowance such as the shared Fable bucket.</summary>
    Model,

    /// <summary>Any other window length reported by the provider.</summary>
    Other
}

/// <summary>
/// Classifies quota windows and derives the short labels printed under the sidebar gauges, so the
/// thumbnail stays readable without relying on colour alone.
/// </summary>
internal static class QuotaWindowLegend
{
    private const string SessionName = "5 小时";
    private const string WeeklyName = "7 天";

    internal static QuotaWindowKind Classify(QuotaWindow window)
    {
        if (window.IsModelScoped) return QuotaWindowKind.Model;
        if (window.Name == SessionName) return QuotaWindowKind.Session;
        if (window.Name == WeeklyName) return QuotaWindowKind.Weekly;
        return QuotaWindowKind.Other;
    }

    /// <summary>
    /// Two- or three-character label: "5h", "7d", the family initial for model allowances
    /// ("Fable 周额度" becomes "F"), or a compacted duration such as "1d" for other windows.
    /// </summary>
    internal static string ShortLabel(QuotaWindow window)
    {
        switch (Classify(window))
        {
            case QuotaWindowKind.Session:
                return "5h";
            case QuotaWindowKind.Weekly:
                return "7d";
            case QuotaWindowKind.Model:
                var family = window.Name.EndsWith(QuotaService.WeeklyScopedSuffix, StringComparison.Ordinal)
                    ? window.Name[..^QuotaService.WeeklyScopedSuffix.Length]
                    : window.Name;
                family = family.Trim();
                if (family.StartsWith("claude-", StringComparison.OrdinalIgnoreCase))
                {
                    family = family["claude-".Length..];
                }
                return family.Length == 0 ? "M" : char.ToUpperInvariant(family[0]).ToString();
            default:
                return window.Name
                    .Replace(" 小时", "h", StringComparison.Ordinal)
                    .Replace(" 天", "d", StringComparison.Ordinal)
                    .Replace(" ", string.Empty, StringComparison.Ordinal);
        }
    }
}
