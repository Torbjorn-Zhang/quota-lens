namespace QuotaLens.Services;

internal sealed record LowQuotaAlert(
    string Provider,
    string WindowName,
    double RemainingPercent);

internal sealed record LowQuotaAlertBatch(
    IReadOnlyList<LowQuotaAlert> Alerts,
    bool StateChanged);

internal static class LowQuotaAlertService
{
    internal static LowQuotaAlertBatch Scan(
        IEnumerable<ProviderQuota> providers,
        ISet<string> notifiedKeys)
    {
        var alerts = new List<LowQuotaAlert>();
        var stateChanged = false;

        foreach (var provider in providers.Where(provider => provider.IsAvailable))
        {
            foreach (var window in provider.Windows)
            {
                var key = CreateKey(provider.Provider, window);
                if (window.RemainingPercent > 20)
                {
                    continue;
                }

                if (!notifiedKeys.Add(key)) continue;
                stateChanged = true;
                alerts.Add(new LowQuotaAlert(
                    provider.Provider,
                    window.Name,
                    window.RemainingPercent));
            }
        }

        return new LowQuotaAlertBatch(alerts, stateChanged);
    }

    private static string CreateKey(string provider, QuotaWindow window)
    {
        var reset = window.ResetsAt?.ToUniversalTime().ToString("O") ?? "unknown";
        return $"{provider}:{window.Name}:{reset}";
    }
}
