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
/// Classifies quota windows for their identity colour and formats the compact reset times shown
/// in the sidebar strip.
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
    /// Reset time in at most five characters of local time: the clock time ("14:30") when the reset
    /// is less than 24 hours away, otherwise the date ("10/3"). "重置中" once the reset has passed
    /// and "—" when the provider did not report one.
    /// </summary>
    internal static string CompactReset(DateTimeOffset? resetsAt, DateTimeOffset now)
    {
        if (resetsAt is not DateTimeOffset reset) return "—";
        var remaining = reset - now;
        if (remaining <= TimeSpan.Zero) return "重置中";

        var local = reset.ToLocalTime();
        return remaining < TimeSpan.FromHours(24)
            ? local.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture)
            : $"{local.Month}/{local.Day}";
    }
}
