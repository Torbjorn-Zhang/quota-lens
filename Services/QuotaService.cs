using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace QuotaLens.Services;

public sealed class QuotaService : IDisposable
{
    private const string CodexUsageUrl = "https://chatgpt.com/backend-api/wham/usage";
    private const string ClaudeUsageUrl = "https://api.anthropic.com/api/oauth/usage";
    private static readonly TimeSpan ClaudeRefreshInterval = TimeSpan.FromMinutes(3);
    internal const string WeeklyScopedSuffix = " 周额度";
    private readonly HttpClient _httpClient;
    private ProviderQuota? _lastClaudeQuota;
    private DateTimeOffset _nextClaudeFetch = DateTimeOffset.MinValue;
    private TimeSpan _claudeBackoff = TimeSpan.FromMinutes(5);
    private bool _claudeRateLimited;

    public QuotaService()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("QuotaLens/0.1 Windows");
    }

    public async Task<QuotaSnapshot> FetchAsync(
        CancellationToken cancellationToken,
        bool forceClaudeRefresh = false)
    {
        var codexTask = FetchCodexSafeAsync(cancellationToken);
        var claudeTask = FetchClaudeManagedAsync(cancellationToken, forceClaudeRefresh);
        await Task.WhenAll(codexTask, claudeTask);
        return new QuotaSnapshot(await codexTask, await claudeTask, DateTimeOffset.Now);
    }

    private async Task<ProviderQuota> FetchCodexSafeAsync(CancellationToken cancellationToken)
    {
        try
        {
            var credential = await CredentialReader.ReadCodexAsync(cancellationToken);
            using var request = new HttpRequestMessage(HttpMethod.Get, CodexUsageUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.AccessToken);
            request.Headers.TryAddWithoutValidation("ChatGPT-Account-Id", credential.AccountId);
            request.Headers.TryAddWithoutValidation("OpenAI-Beta", "codex-1");
            request.Headers.TryAddWithoutValidation("originator", "codex_cli_rs");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            await EnsureSuccessAsync(response, "Codex", cancellationToken);
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            return ParseCodex(json.RootElement);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ProviderQuota.Failed("Codex", FriendlyError(ex, "Codex"));
        }
    }

    private async Task<ProviderQuota> FetchClaudeManagedAsync(
        CancellationToken cancellationToken,
        bool forceRefresh)
    {
        var now = DateTimeOffset.Now;
        if (now < _nextClaudeFetch && (!forceRefresh || _claudeRateLimited))
        {
            if (_lastClaudeQuota is not null) return _lastClaudeQuota;
            var wait = FormatRetryWait(_nextClaudeFetch - now);
            return ProviderQuota.Failed("Claude Code", $"Claude 查询暂时受限，{wait}后自动重试。");
        }

        try
        {
            var credential = await CredentialReader.ReadClaudeAsync(cancellationToken);
            using var request = new HttpRequestMessage(HttpMethod.Get, ClaudeUsageUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.AccessToken);
            request.Headers.TryAddWithoutValidation("anthropic-beta", "oauth-2025-04-20");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            await EnsureSuccessAsync(response, "Claude", cancellationToken);
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var quota = ParseClaude(json.RootElement) with
            {
                Plan = FormatClaudePlan(credential.SubscriptionType, credential.RateLimitTier)
            };
            _lastClaudeQuota = quota;
            _nextClaudeFetch = now + ClaudeRefreshInterval;
            _claudeBackoff = TimeSpan.FromMinutes(5);
            _claudeRateLimited = false;
            return quota;
        }
        catch (QuotaRateLimitException ex)
        {
            var serverWait = ex.RetryAfter ?? TimeSpan.Zero;
            var wait = serverWait > _claudeBackoff ? serverWait : _claudeBackoff;
            _nextClaudeFetch = DateTimeOffset.Now + wait;
            _claudeBackoff = TimeSpan.FromMinutes(Math.Min(30, _claudeBackoff.TotalMinutes * 2));
            _claudeRateLimited = true;

            var status = $"Claude 查询暂时限流，保留上次数据 · {FormatRetryWait(wait)}后重试";
            if (_lastClaudeQuota is not null)
            {
                return _lastClaudeQuota with
                {
                    ExtraInfo = JoinExtraInfo(_lastClaudeQuota.ExtraInfo, status)
                };
            }

            return ProviderQuota.Failed("Claude Code", $"Claude 查询暂时受限，{FormatRetryWait(wait)}后自动重试。");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ProviderQuota.Failed("Claude Code", FriendlyError(ex, "Claude Code"));
        }
    }

    internal static ProviderQuota ParseCodex(JsonElement root)
    {
        var windows = new List<QuotaWindow>();
        if (TryObject(root, "rate_limit", out var rateLimit))
        {
            AddCodexWindow(windows, rateLimit, "primary_window", "5 小时");
            AddCodexWindow(windows, rateLimit, "secondary_window", "7 天");
        }

        if (windows.Count == 0)
        {
            throw new QuotaException("Codex 返回了额度信息，但没有可识别的额度窗口。");
        }

        var plan = GetString(root, "plan_type") ?? "ChatGPT";
        string? extra = null;
        if (TryObject(root, "credits", out var credits))
        {
            var unlimited = GetBoolean(credits, "unlimited");
            var balance = GetFlexibleString(credits, "balance");
            extra = unlimited == true
                ? "附加 credits：不限量"
                : !string.IsNullOrWhiteSpace(balance) ? $"附加 credits：{balance}" : null;
        }

        return new ProviderQuota("Codex", plan, windows, extra);
    }

    internal static ProviderQuota ParseClaude(JsonElement root)
    {
        var windows = new List<QuotaWindow>();
        AddClaudeWindow(windows, root, "five_hour", "5 小时");
        AddClaudeWindow(windows, root, "seven_day", "7 天");
        AddClaudeScopedWindows(windows, root);

        if (windows.Count == 0)
        {
            throw new QuotaException("Claude 返回了额度信息，但没有可识别的额度窗口。");
        }

        var modelParts = new List<string>();
        AddClaudeModelInfo(modelParts, root, "seven_day_sonnet", "Sonnet 周额度");
        AddClaudeModelInfo(modelParts, root, "seven_day_opus", "Opus 周额度");

        if (TryObject(root, "extra_usage", out var extraUsage)
            && GetBoolean(extraUsage, "is_enabled") == true)
        {
            var used = GetFlexibleString(extraUsage, "used_credits");
            var limit = GetFlexibleString(extraUsage, "monthly_limit");
            if (!string.IsNullOrWhiteSpace(used) || !string.IsNullOrWhiteSpace(limit))
            {
                modelParts.Add($"额外用量 {used ?? "0"} / {limit ?? "—"}");
            }
        }

        return new ProviderQuota(
            "Claude Code",
            "Claude 订阅",
            windows,
            modelParts.Count > 0 ? string.Join(" · ", modelParts) : null);
    }

    internal static string FormatClaudePlan(string? subscriptionType, string? rateLimitTier)
    {
        var tier = rateLimitTier?.Trim().ToLowerInvariant();
        if (tier?.Contains("max_20x", StringComparison.Ordinal) == true) return "Max 20×";
        if (tier?.Contains("max_5x", StringComparison.Ordinal) == true) return "Max 5×";

        return subscriptionType?.Trim().ToLowerInvariant() switch
        {
            "max" => "Max",
            "pro" => "Pro",
            "team" => "Team",
            "enterprise" => "Enterprise",
            "free" => "Free",
            _ => "Claude 订阅"
        };
    }

    private static void AddCodexWindow(
        ICollection<QuotaWindow> result,
        JsonElement parent,
        string property,
        string fallbackName)
    {
        if (!TryObject(parent, property, out var window)) return;
        var used = GetNumber(window, "used_percent");
        if (used is null) return;

        var seconds = GetNumber(window, "limit_window_seconds");
        var name = seconds switch
        {
            >= 17_400 and <= 18_600 => "5 小时",
            >= 601_000 and <= 610_000 => "7 天",
            > 0 => FormatWindow(seconds.Value),
            _ => fallbackName
        };
        result.Add(new QuotaWindow(name, used.Value, GetUnixTime(window, "reset_at")));
    }

    private static void AddClaudeWindow(
        ICollection<QuotaWindow> result,
        JsonElement root,
        string property,
        string name)
    {
        if (!TryObject(root, property, out var window)) return;
        var used = GetNumber(window, "utilization");
        if (used is null) return;
        result.Add(new QuotaWindow(name, used.Value, GetDateTime(window, "resets_at")));
    }

    /// <summary>
    /// Adds one window per model family found in <c>limits[]</c> (<c>kind == "weekly_scoped"</c>).
    /// The usage API reports these buckets per family rather than per model: as of 2026-09-02 a single
    /// "Fable" scope (with a null <c>model.id</c>) covers both Fable 5 and Fable 5.1. Every family is
    /// surfaced so that an upstream split into separate buckets shows up as an extra row automatically.
    /// Entries that share a display name (for example per-surface duplicates) collapse into the most
    /// used one, which is the conservative reading for a remaining-percentage display.
    /// </summary>
    private static void AddClaudeScopedWindows(
        ICollection<QuotaWindow> result,
        JsonElement root)
    {
        if (!root.TryGetProperty("limits", out var limits)
            || limits.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var candidates = new List<(string Name, double Used, DateTimeOffset? ResetsAt, bool IsActive)>();
        foreach (var limit in limits.EnumerateArray())
        {
            if (limit.ValueKind != JsonValueKind.Object
                || !string.Equals(GetString(limit, "kind"), "weekly_scoped", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var used = GetNumber(limit, "percent") ?? GetNumber(limit, "utilization");
            if (used is null
                || !TryObject(limit, "scope", out var scope)
                || !TryObject(scope, "model", out var model))
            {
                continue;
            }

            var displayName = GetString(model, "display_name");
            if (string.IsNullOrWhiteSpace(displayName)) displayName = GetString(model, "id");
            if (string.IsNullOrWhiteSpace(displayName)) continue;

            candidates.Add((
                displayName.Trim(),
                used.Value,
                GetDateTime(limit, "resets_at"),
                GetBoolean(limit, "is_active") == true));
        }

        var families = candidates
            .GroupBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var mostUsed = group
                    .OrderByDescending(candidate => candidate.Used)
                    .ThenByDescending(candidate => candidate.IsActive)
                    .ThenBy(candidate => candidate.Name, StringComparer.Ordinal)
                    .First();
                return (
                    // Pick the casing deterministically; GroupBy's Key would follow array order.
                    Name: group.Select(candidate => candidate.Name).OrderBy(name => name, StringComparer.Ordinal).First(),
                    mostUsed.Used,
                    mostUsed.ResetsAt,
                    IsActive: group.Any(candidate => candidate.IsActive));
            })
            .OrderByDescending(family =>
                family.Name.Contains("Fable", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(family => family.IsActive)
            .ThenByDescending(family => family.Used)
            .ThenBy(family => family.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var family in families)
        {
            result.Add(new QuotaWindow(
                family.Name + WeeklyScopedSuffix,
                family.Used,
                family.ResetsAt,
                IsModelScoped: true));
        }
    }

    private static void AddClaudeModelInfo(
        ICollection<string> result,
        JsonElement root,
        string property,
        string label)
    {
        if (!TryObject(root, property, out var window)) return;
        var used = GetNumber(window, "utilization");
        if (used is null) return;
        result.Add($"{label}剩 {Math.Clamp(100d - used.Value, 0, 100):0}%");
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        string provider,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new QuotaException($"{provider} 登录已过期，请在客户端中重新登录。");
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var retryAfter = response.Headers.RetryAfter?.Delta;
            if (retryAfter is null && response.Headers.RetryAfter?.Date is DateTimeOffset retryDate)
            {
                retryAfter = retryDate - DateTimeOffset.Now;
            }
            throw new QuotaRateLimitException(retryAfter > TimeSpan.Zero ? retryAfter : null);
        }

        await response.Content.LoadIntoBufferAsync();
        cancellationToken.ThrowIfCancellationRequested();
        throw new QuotaException($"{provider} 服务暂时不可用（HTTP {(int)response.StatusCode}），稍后会自动重试。");
    }

    private static string FriendlyError(Exception ex, string provider) => ex switch
    {
        QuotaException => ex.Message,
        TaskCanceledException => $"连接 {provider} 超时，稍后会自动重试。",
        HttpRequestException => $"无法连接 {provider}，请检查网络或代理设置。",
        JsonException => $"{provider} 返回格式已变化，请更新 Quota Lens。",
        _ => $"读取 {provider} 额度失败：{ex.Message}"
    };

    private static string FormatWindow(double seconds)
    {
        var span = TimeSpan.FromSeconds(seconds);
        return span.TotalDays >= 1 ? $"{span.TotalDays:0} 天" : $"{span.TotalHours:0} 小时";
    }

    private static string FormatRetryWait(TimeSpan wait)
    {
        var minutes = Math.Max(1, (int)Math.Ceiling(wait.TotalMinutes));
        return minutes >= 60 ? $"{minutes / 60}小时{minutes % 60}分" : $"约{minutes}分钟";
    }

    private static string JoinExtraInfo(string? existing, string status) =>
        string.IsNullOrWhiteSpace(existing) ? status : $"{existing} · {status}";

    private static bool TryObject(JsonElement parent, string name, out JsonElement value)
    {
        if (parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(name, out value)
            && value.ValueKind == JsonValueKind.Object)
        {
            return true;
        }
        value = default;
        return false;
    }

    private static double? GetNumber(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)) return number;
        if (value.ValueKind == JsonValueKind.String
            && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number))
        {
            return number;
        }
        return null;
    }

    private static string? GetString(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? GetFlexibleString(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    private static bool? GetBoolean(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value)
        && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    private static DateTimeOffset? GetUnixTime(JsonElement parent, string name)
    {
        var value = GetNumber(parent, name);
        if (value is null) return null;
        try { return DateTimeOffset.FromUnixTimeSeconds(Convert.ToInt64(value.Value)); }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    private static DateTimeOffset? GetDateTime(JsonElement parent, string name)
    {
        var value = GetString(parent, name);
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)
            ? date
            : null;
    }

    public void Dispose() => _httpClient.Dispose();
}

internal sealed class QuotaRateLimitException : Exception
{
    public QuotaRateLimitException(TimeSpan? retryAfter)
        : base("Usage endpoint rate limited")
    {
        RetryAfter = retryAfter;
    }

    public TimeSpan? RetryAfter { get; }
}
