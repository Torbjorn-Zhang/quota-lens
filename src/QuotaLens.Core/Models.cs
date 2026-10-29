using System.Text.Json.Serialization;

namespace QuotaLens;

/// <summary>Screen edge the widget is docked to as a sidebar; <see cref="None"/> means floating.</summary>
public enum DockEdge
{
    None,
    Left,
    Right
}

public sealed record QuotaWindow(
    string Name,
    double UsedPercent,
    DateTimeOffset? ResetsAt,
    bool IsModelScoped = false)
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

    /// <summary>Plan-wide windows such as the 5-hour and 7-day allowances.</summary>
    public IReadOnlyList<QuotaWindow> StandardWindows =>
        Windows.Where(window => !window.IsModelScoped).ToList();

    /// <summary>Per-model-family weekly allowances (for example the shared Fable bucket).</summary>
    public IReadOnlyList<QuotaWindow> ModelScopedWindows =>
        Windows.Where(window => window.IsModelScoped).ToList();

    /// <summary>When these numbers were fetched; null means at the snapshot's own time.</summary>
    public DateTimeOffset? UpdatedAt { get; init; }

    /// <summary>Set while the last good numbers are kept during a 429 backoff; says when it retries.</summary>
    public string? StaleNote { get; init; }

    /// <summary>True while the macOS keychain authorisation prompt is still open.</summary>
    public bool IsWaiting { get; init; }

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

    /// <summary>Write a sanitised quota.json after every refresh (see QuotaSnapshotExport). Off by default.</summary>
    public bool ExportQuotaSnapshot { get; set; }

    public double WidgetOpacity { get; set; } = 0.74;
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }

    /// <summary>Settings files written before 0.5.0 lack this key and therefore start docked right.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public DockEdge DockEdge { get; set; } = DockEdge.Right;

    public List<string> NotifiedLowQuotaKeys { get; set; } = new();
}
