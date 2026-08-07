namespace QuotaLens;

public sealed record QuotaWindow(
    string Name,
    double UsedPercent,
    DateTimeOffset? ResetsAt)
{
    public double RemainingPercent => Math.Clamp(100d - UsedPercent, 0d, 100d);
}

public sealed record ProviderQuota(
    string Provider,
    string Plan,
    IReadOnlyList<QuotaWindow> Windows,
    string? ExtraInfo = null,
    string? Error = null)
{
    public bool IsAvailable => string.IsNullOrWhiteSpace(Error) && Windows.Count > 0;

    public static ProviderQuota Failed(string provider, string error) =>
        new(provider, string.Empty, Array.Empty<QuotaWindow>(), null, error);
}

public sealed record QuotaSnapshot(
    ProviderQuota Codex,
    ProviderQuota Claude,
    DateTimeOffset FetchedAt);

public sealed class AppSettings
{
    public int PollSeconds { get; set; } = 60;
    public bool StartWithWindows { get; set; }
    public bool AlwaysOnTop { get; set; }
    public bool LowQuotaNotificationsEnabled { get; set; } = true;
    public double WidgetOpacity { get; set; } = 0.74;
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public List<string> NotifiedLowQuotaKeys { get; set; } = new();
}
