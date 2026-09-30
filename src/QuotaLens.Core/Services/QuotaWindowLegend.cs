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
/// Classifies quota windows, holds the colour palette shared by the Windows and macOS apps, and
/// formats reset times.
/// </summary>
internal static class QuotaWindowLegend
{
    private const string SessionName = "5 小时";
    private const string WeeklyName = "7 天";

    internal const string CriticalHex = "#FF6B7A";
    internal const string WarningHex = "#FFB454";

    /// <summary>Blue 5-hour, violet 7-day, green model allowance, grey-blue for anything else.</summary>
    internal static string IdentityHex(QuotaWindow window) => Classify(window) switch
    {
        QuotaWindowKind.Session => "#4CC9F0",
        QuotaWindowKind.Weekly => "#B18CFF",
        QuotaWindowKind.Model => "#38D6A3",
        _ => "#9DB2C8"
    };

    /// <summary>Bars and rings keep their identity colour until the window is nearly exhausted.</summary>
    internal static string BarHex(QuotaWindow window) =>
        window.RemainingPercent <= 20 ? CriticalHex : IdentityHex(window);

    /// <summary>Warning colour for percentages (orange at 40% or less, red at 20% or less), else null.</summary>
    internal static string? LevelHex(double remaining) => remaining switch
    {
        <= 20 => CriticalHex,
        <= 40 => WarningHex,
        _ => null
    };

    /// <summary>Countdown plus local reset time, e.g. "2时19分后 · 10/3 02:49".</summary>
    internal static string FormatResetCountdown(DateTimeOffset? resetAt, DateTimeOffset now)
    {
        if (resetAt is null) return "重置时间未知";
        var remaining = resetAt.Value - now;
        if (remaining <= TimeSpan.Zero) return "额度窗口正在重置";

        var countdown = remaining.TotalDays >= 1
            ? $"{(int)remaining.TotalDays}天{remaining.Hours}时"
            : remaining.TotalHours >= 1
                ? $"{(int)remaining.TotalHours}时{remaining.Minutes}分"
                : $"{Math.Max(0, remaining.Minutes)}分{Math.Max(0, remaining.Seconds)}秒";
        return $"{countdown}后 · {resetAt.Value.LocalDateTime:M/d HH:mm}";
    }

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
