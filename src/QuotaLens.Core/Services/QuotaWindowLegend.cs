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

    // Identity colours for dark surfaces. Every pair stays at least OKLab ΔE 19 apart for full-colour
    // vision and 9.5 under simulated protanopia and deuteranopia, none resembles the warning orange
    // or critical red, and each keeps 4.5:1 against the dark glass even with a white desktop or
    // wallpaper showing through at the densities the apps use.
    internal const string SessionHex = "#71C9FA";
    internal const string WeeklyHex = "#BF83FE";
    internal const string ModelHex = "#3EEE92";
    internal const string OtherHex = "#9DB2C8";

    /// <summary>Sky blue 5-hour, orchid 7-day, mint model allowance, grey-blue for anything else.</summary>
    internal static string IdentityHex(QuotaWindow window) => Classify(window) switch
    {
        QuotaWindowKind.Session => SessionHex,
        QuotaWindowKind.Weekly => WeeklyHex,
        QuotaWindowKind.Model => ModelHex,
        _ => OtherHex
    };

    /// <summary>
    /// Counterpart of a palette colour for light surfaces (the light macOS menu bar): deep enough for
    /// 4.5:1 text on white, with the blue and the purple far enough apart in lightness to stay
    /// distinct for red-green colour-blind readers.
    /// </summary>
    internal static string OnLightHex(string hex) => hex.ToUpperInvariant() switch
    {
        SessionHex => "#0E2FFD",
        WeeklyHex => "#A211AC",
        ModelHex => "#1D8071",
        WarningHex => "#A85400",
        CriticalHex => "#C8283E",
        _ => "#4E5A6E"
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

    /// <summary>
    /// Countdown short enough for the macOS menu bar: "42m" under an hour, "2h05m" under a day,
    /// otherwise "2d14h". "0m" once the reset has passed and "—" when none was reported.
    /// </summary>
    internal static string CompactCountdown(DateTimeOffset? resetsAt, DateTimeOffset now)
    {
        if (resetsAt is not DateTimeOffset reset) return "—";
        var remaining = reset - now;
        if (remaining <= TimeSpan.Zero) return "0m";
        if (remaining < TimeSpan.FromHours(1)) return $"{Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes))}m";
        if (remaining < TimeSpan.FromDays(1)) return $"{(int)remaining.TotalHours}h{remaining.Minutes:00}m";
        return $"{(int)remaining.TotalDays}d{remaining.Hours}h";
    }

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
