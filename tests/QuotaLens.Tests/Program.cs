using System.Text.Json;
using QuotaLens;
using QuotaLens.Services;

var failures = new List<string>();

Run("Codex parser", () =>
{
    using var json = JsonDocument.Parse(@"{
      ""plan_type"": ""pro"",
      ""rate_limit"": {
        ""primary_window"": {
          ""used_percent"": 37,
          ""limit_window_seconds"": 18000,
          ""reset_at"": 1784200000
        },
        ""secondary_window"": {
          ""used_percent"": 62,
          ""limit_window_seconds"": 604800,
          ""reset_at"": 1784600000
        }
      },
      ""credits"": {
        ""has_credits"": true,
        ""unlimited"": false,
        ""balance"": ""9.99""
      }
    }");

    var quota = QuotaService.ParseCodex(json.RootElement);
    Equal("pro", quota.Plan);
    Equal(2, quota.Windows.Count);
    Equal("5 小时", quota.Windows[0].Name);
    Equal(63d, quota.Windows[0].RemainingPercent);
    Equal("7 天", quota.Windows[1].Name);
    Equal(38d, quota.Windows[1].RemainingPercent);
    Equal("附加 credits：9.99", quota.ExtraInfo);
});

Run("Codex restored 5-hour quota parser", () =>
{
    using var weeklyOnlyJson = JsonDocument.Parse(@"{
      ""plan_type"": ""plus"",
      ""rate_limit"": {
        ""primary_window"": null,
        ""secondary_window"": {
          ""used_percent"": 24,
          ""limit_window_seconds"": 604800,
          ""reset_at"": 1784600000
        }
      }
    }");
    var weeklyOnly = QuotaService.ParseCodex(weeklyOnlyJson.RootElement);
    Equal(1, weeklyOnly.Windows.Count);
    Equal("7 天", weeklyOnly.Windows[0].Name);

    using var restoredJson = JsonDocument.Parse(@"{
      ""plan_type"": ""plus"",
      ""rate_limit"": {
        ""primary_window"": {
          ""used_percent"": 100,
          ""limit_window_seconds"": 18000,
          ""reset_at"": 1784218000
        },
        ""secondary_window"": {
          ""used_percent"": 24,
          ""limit_window_seconds"": 604800,
          ""reset_at"": 1784600000
        }
      }
    }");
    var restored = QuotaService.ParseCodex(restoredJson.RootElement);
    Equal(2, restored.Windows.Count);
    Equal("5 小时", restored.Windows[0].Name);
    Equal(0d, restored.Windows[0].RemainingPercent);
    Equal("7 天", restored.Windows[1].Name);
    Equal(76d, restored.Windows[1].RemainingPercent);
});

Run("Claude parser", () =>
{
    using var json = JsonDocument.Parse(@"{
      ""five_hour"": {
        ""utilization"": 18,
        ""resets_at"": ""2026-07-16T15:00:00+00:00""
      },
      ""seven_day"": {
        ""utilization"": 41,
        ""resets_at"": ""2026-07-20T15:00:00+00:00""
      },
      ""seven_day_sonnet"": {
        ""utilization"": 12,
        ""resets_at"": ""2026-07-20T15:00:00+00:00""
      },
      ""seven_day_opus"": null,
      ""extra_usage"": {
        ""is_enabled"": true,
        ""monthly_limit"": 100,
        ""used_credits"": 23.5
      }
    }");

    var quota = QuotaService.ParseClaude(json.RootElement);
    Equal(2, quota.Windows.Count);
    Equal(82d, quota.Windows[0].RemainingPercent);
    Equal(59d, quota.Windows[1].RemainingPercent);
    Contains("Sonnet 周额度剩 88%", quota.ExtraInfo);
    Contains("额外用量 23.5 / 100", quota.ExtraInfo);
});

Run("Claude Max plan formatter", () =>
{
    Equal("Max 20×", QuotaService.FormatClaudePlan("max", "default_claude_max_20x"));
    Equal("Max 5×", QuotaService.FormatClaudePlan("max", "default_claude_max_5x"));
    Equal("Max", QuotaService.FormatClaudePlan("max", null));
    Equal("Pro", QuotaService.FormatClaudePlan("pro", null));
    Equal("Claude 订阅", QuotaService.FormatClaudePlan(null, null));
});

Run("Claude Fable scoped quota parser", () =>
{
    using var json = JsonDocument.Parse(@"{
      ""five_hour"": {
        ""utilization"": 18,
        ""resets_at"": ""2026-07-23T15:00:00+00:00""
      },
      ""seven_day"": {
        ""utilization"": 41,
        ""resets_at"": ""2026-07-27T15:00:00+00:00""
      },
      ""limits"": [
        {
          ""kind"": ""weekly_scoped"",
          ""percent"": 72,
          ""resets_at"": ""2026-07-27T15:00:00+00:00"",
          ""scope"": {
            ""model"": {
              ""id"": ""claude-opus-4-8"",
              ""display_name"": ""Opus""
            }
          },
          ""is_active"": true
        },
        {
          ""kind"": ""weekly_scoped"",
          ""utilization"": 54,
          ""resets_at"": ""2026-07-28T16:30:00+00:00"",
          ""scope"": {
            ""model"": {
              ""id"": ""claude-fable-5"",
              ""display_name"": ""Fable""
            }
          },
          ""is_active"": false
        }
      ]
    }");

    var quota = QuotaService.ParseClaude(json.RootElement);
    Equal(4, quota.Windows.Count);
    Equal(2, quota.StandardWindows.Count);
    Equal(2, quota.ModelScopedWindows.Count);
    Equal(false, quota.Windows[0].IsModelScoped);
    Equal(false, quota.Windows[1].IsModelScoped);
    Equal("Fable 周额度", quota.Windows[2].Name);
    Equal(true, quota.Windows[2].IsModelScoped);
    Equal(46d, quota.Windows[2].RemainingPercent);
    Equal(
        DateTimeOffset.Parse("2026-07-28T16:30:00+00:00"),
        quota.Windows[2].ResetsAt);
    Equal("Opus 周额度", quota.Windows[3].Name);
    Equal(28d, quota.Windows[3].RemainingPercent);
});

Run("Claude Fable family bucket shared by Fable 5 and 5.1", () =>
{
    // Shape observed live on 2026-09-02, after the Fable 5.1 launch: one family-level "Fable"
    // scope with a null model id, next to non-scoped session and weekly_all entries.
    using var json = JsonDocument.Parse(@"{
      ""five_hour"": { ""utilization"": 6.0, ""resets_at"": ""2026-09-02T09:00:00.934244+00:00"" },
      ""seven_day"": { ""utilization"": 1.0, ""resets_at"": ""2026-09-05T06:00:00.934269+00:00"" },
      ""seven_day_opus"": null,
      ""seven_day_sonnet"": null,
      ""limits"": [
        { ""kind"": ""session"", ""percent"": 6, ""resets_at"": ""2026-09-02T09:00:00.934244+00:00"",
          ""scope"": null, ""is_active"": true },
        { ""kind"": ""weekly_all"", ""percent"": 1, ""resets_at"": ""2026-09-05T06:00:00.934269+00:00"",
          ""scope"": null, ""is_active"": false },
        { ""kind"": ""weekly_scoped"", ""percent"": 2, ""resets_at"": ""2026-09-05T06:00:00.934492+00:00"",
          ""scope"": { ""model"": { ""id"": null, ""display_name"": ""Fable"" }, ""surface"": null },
          ""is_active"": false }
      ]
    }");

    var quota = QuotaService.ParseClaude(json.RootElement);
    Equal(3, quota.Windows.Count);
    Equal(2, quota.StandardWindows.Count);
    Equal(1, quota.ModelScopedWindows.Count);
    Equal("Fable 周额度", quota.ModelScopedWindows[0].Name);
    Equal(98d, quota.ModelScopedWindows[0].RemainingPercent);
    Equal(
        DateTimeOffset.Parse("2026-09-05T06:00:00.934492+00:00"),
        quota.ModelScopedWindows[0].ResetsAt);
});

Run("Split Fable buckets render as separate rows", () =>
{
    // Hypothetical upstream change: Fable 5 and Fable 5.1 get their own buckets. Both must appear,
    // Fable-family rows first, without any parser change.
    using var json = JsonDocument.Parse(@"{
      ""five_hour"": { ""utilization"": 10, ""resets_at"": ""2026-09-02T09:00:00+00:00"" },
      ""seven_day"": { ""utilization"": 20, ""resets_at"": ""2026-09-05T06:00:00+00:00"" },
      ""limits"": [
        { ""kind"": ""weekly_scoped"", ""percent"": 40, ""resets_at"": ""2026-09-05T06:00:00+00:00"",
          ""scope"": { ""model"": { ""id"": ""claude-opus-5"", ""display_name"": ""Opus"" } }, ""is_active"": true },
        { ""kind"": ""weekly_scoped"", ""percent"": 30, ""resets_at"": ""2026-09-05T06:00:00+00:00"",
          ""scope"": { ""model"": { ""id"": ""claude-fable-5-1"", ""display_name"": ""Fable 5.1"" } }, ""is_active"": false },
        { ""kind"": ""weekly_scoped"", ""percent"": 10, ""resets_at"": ""2026-09-05T06:00:00+00:00"",
          ""scope"": { ""model"": { ""id"": ""claude-fable-5"", ""display_name"": ""Fable 5"" } }, ""is_active"": false }
      ]
    }");

    var quota = QuotaService.ParseClaude(json.RootElement);
    var scoped = quota.ModelScopedWindows;
    Equal(3, scoped.Count);
    Equal("Fable 5.1 周额度", scoped[0].Name);
    Equal(70d, scoped[0].RemainingPercent);
    Equal("Fable 5 周额度", scoped[1].Name);
    Equal(90d, scoped[1].RemainingPercent);
    Equal("Opus 周额度", scoped[2].Name);
    Equal(60d, scoped[2].RemainingPercent);
});

Run("Duplicate scoped buckets collapse to the most used entry", () =>
{
    using var json = JsonDocument.Parse(@"{
      ""five_hour"": { ""utilization"": 10, ""resets_at"": ""2026-09-02T09:00:00+00:00"" },
      ""seven_day"": { ""utilization"": 20, ""resets_at"": ""2026-09-05T06:00:00+00:00"" },
      ""limits"": [
        { ""kind"": ""weekly_scoped"", ""percent"": 35, ""resets_at"": ""2026-09-05T06:00:00+00:00"",
          ""scope"": { ""model"": { ""display_name"": ""fable"" }, ""surface"": ""cowork"" }, ""is_active"": false },
        { ""kind"": ""weekly_scoped"", ""percent"": 20, ""resets_at"": ""2026-09-06T06:00:00+00:00"",
          ""scope"": { ""model"": { ""display_name"": ""Fable"" }, ""surface"": ""claude_code"" }, ""is_active"": true }
      ]
    }");

    var quota = QuotaService.ParseClaude(json.RootElement);
    Equal(1, quota.ModelScopedWindows.Count);
    // Casing is chosen deterministically (ordinal minimum), not by array order.
    Equal("Fable 周额度", quota.ModelScopedWindows[0].Name);
    // Percent and reset time come from the most used duplicate even when it is not the active one.
    Equal(65d, quota.ModelScopedWindows[0].RemainingPercent);
    Equal(
        DateTimeOffset.Parse("2026-09-05T06:00:00+00:00"),
        quota.ModelScopedWindows[0].ResetsAt);
});

Run("Scoped families order by activity, usage, then name", () =>
{
    using var json = JsonDocument.Parse(@"{
      ""five_hour"": { ""utilization"": 10, ""resets_at"": ""2026-09-02T09:00:00+00:00"" },
      ""seven_day"": { ""utilization"": 20, ""resets_at"": ""2026-09-05T06:00:00+00:00"" },
      ""limits"": [
        { ""kind"": ""weekly_scoped"", ""percent"": 60, ""resets_at"": ""2026-09-05T06:00:00+00:00"",
          ""scope"": { ""model"": { ""display_name"": ""Opus"" } }, ""is_active"": false },
        { ""kind"": ""weekly_scoped"", ""percent"": 20, ""resets_at"": ""2026-09-05T06:00:00+00:00"",
          ""scope"": { ""model"": { ""display_name"": ""Sonnet"" } }, ""is_active"": true },
        { ""kind"": ""weekly_scoped"", ""percent"": 20, ""resets_at"": ""2026-09-05T06:00:00+00:00"",
          ""scope"": { ""model"": { ""display_name"": ""Haiku"" } }, ""is_active"": true },
        { ""kind"": ""weekly_scoped"", ""percent"": 30, ""resets_at"": ""2026-09-05T06:00:00+00:00"",
          ""scope"": { ""model"": { ""id"": ""claude-fable-5-1"", ""display_name"": """" } }, ""is_active"": false }
      ]
    }");

    var scoped = QuotaService.ParseClaude(json.RootElement).ModelScopedWindows;
    Equal(4, scoped.Count);
    // Blank display_name falls back to the model id; the id still counts as Fable family and sorts first.
    Equal("claude-fable-5-1 周额度", scoped[0].Name);
    // Active buckets beat inactive ones regardless of usage; equal usage falls back to name order.
    Equal("Haiku 周额度", scoped[1].Name);
    Equal("Sonnet 周额度", scoped[2].Name);
    Equal("Opus 周额度", scoped[3].Name);
});

Run("Raw usage dump masks identifiers", () =>
{
    using var json = JsonDocument.Parse(@"{
      ""email"": ""a@b.c"",
      ""organization"": { ""name"": ""Org"", ""display_name"": ""Person"" },
      ""limits"": [
        { ""kind"": ""weekly_scoped"", ""percent"": 2, ""resets_at"": ""2026-09-05T06:00:00+00:00"",
          ""scope"": { ""model"": { ""id"": ""claude-fable-5"", ""display_name"": ""Fable"" } } }
      ]
    }");

    var dump = RawUsageDump.Describe(json.RootElement);
    Contains("\"<string len=5>\"", dump);
    Contains("\"<string len=3>\"", dump);
    Contains("\"<string len=6>\"", dump);
    Contains("\"weekly_scoped\"", dump);
    Contains("\"2026-09-05T06:00:00+00:00\"", dump);
    Contains("\"claude-fable-5\"", dump);
    Contains("\"Fable\"", dump);
    Equal(false, dump.Contains("a@b.c", StringComparison.Ordinal));
    Equal(false, dump.Contains("Org", StringComparison.Ordinal));
    Equal(false, dump.Contains("Person", StringComparison.Ordinal));
});

Run("Missing windows fail clearly", () =>
{
    using var json = JsonDocument.Parse("{\"plan_type\":\"plus\",\"rate_limit\":null}");
    try
    {
        QuotaService.ParseCodex(json.RootElement);
        throw new Exception("Expected QuotaException was not thrown.");
    }
    catch (QuotaException)
    {
        // Expected.
    }
});

Run("Claude Desktop chooses newest profile token", () =>
{
    using var json = JsonDocument.Parse(@"{
      ""account-a:https://api.anthropic.com:user:profile"": {
        ""token"": ""older-token"",
        ""refreshToken"": ""older-refresh"",
        ""expiresAt"": 100
      },
      ""account-b:https://api.anthropic.com:user:inference user:profile user:sessions:claude_code"": {
        ""token"": ""newer-token"",
        ""refreshToken"": ""newer-refresh"",
        ""expiresAt"": 200,
        ""subscriptionType"": ""max"",
        ""rateLimitTier"": ""default_claude_max_20x""
      }
    }");

    Equal("newer-token", CredentialReader.FindClaudeDesktopToken(json.RootElement));
    var credential = CredentialReader.FindClaudeDesktopCredential(json.RootElement);
    Equal("max", credential?.SubscriptionType);
    Equal("default_claude_max_20x", credential?.RateLimitTier);
});

Run("Low quota alerts are coalesced and persisted", () =>
{
    var reset = DateTimeOffset.Parse("2026-08-10T12:00:00+00:00");
    var notified = new HashSet<string>(StringComparer.Ordinal);
    var providers = new[]
    {
        new ProviderQuota("Codex", "plus", new[]
        {
            new QuotaWindow("5 小时", 92, reset)
        }),
        new ProviderQuota("Claude Code", "max", new[]
        {
            new QuotaWindow("Fable 周额度", 95, reset)
        })
    };

    var first = LowQuotaAlertService.Scan(providers, notified);
    Equal(2, first.Alerts.Count);
    Equal(true, first.StateChanged);

    var sameProcess = LowQuotaAlertService.Scan(providers, notified);
    Equal(0, sameProcess.Alerts.Count);
    Equal(false, sameProcess.StateChanged);

    var jitteredReset = new[]
    {
        new ProviderQuota("Claude Code", "max", new[]
        {
            new QuotaWindow("Fable 周额度", 95, reset.AddMilliseconds(350))
        })
    };
    var timestampJitter = LowQuotaAlertService.Scan(jitteredReset, notified);
    Equal(0, timestampJitter.Alerts.Count);

    var negativeTimestampJitter = new[]
    {
        new ProviderQuota("Claude Code", "max", new[]
        {
            new QuotaWindow("Fable 周额度", 95, reset.AddMilliseconds(-350))
        })
    };
    var timestampJitterBeforeMinute = LowQuotaAlertService.Scan(negativeTimestampJitter, notified);
    Equal(0, timestampJitterBeforeMinute.Alerts.Count);

    var temporaryRecovery = new[]
    {
        new ProviderQuota("Claude Code", "max", new[]
        {
            new QuotaWindow("Fable 周额度", 70, reset)
        })
    };
    LowQuotaAlertService.Scan(temporaryRecovery, notified);
    var lowAgain = LowQuotaAlertService.Scan(providers, notified);
    Equal(0, lowAgain.Alerts.Count);

    var afterRestart = LowQuotaAlertService.Scan(
        providers,
        new HashSet<string>(notified, StringComparer.Ordinal));
    Equal(0, afterRestart.Alerts.Count);

    var nextWindow = new[]
    {
        new ProviderQuota("Claude Code", "max", new[]
        {
            new QuotaWindow("Fable 周额度", 95, reset.AddDays(7))
        })
    };
    var afterReset = LowQuotaAlertService.Scan(nextWindow, notified);
    Equal(1, afterReset.Alerts.Count);
});

if (args.Contains("--live", StringComparer.OrdinalIgnoreCase))
{
    await RunLiveAsync();
}

if (args.Contains("--raw-usage", StringComparer.OrdinalIgnoreCase))
{
    try
    {
        await RawUsageDump.RunAsync();
    }
    catch (Exception ex)
    {
        failures.Add($"Raw usage dump: {ex.GetType().Name}: {ex.Message}");
    }
}

if (args.Contains("--desktop-shape", StringComparer.OrdinalIgnoreCase))
{
    await PrintDesktopShapeAsync();
}

if (failures.Count > 0)
{
    Console.Error.WriteLine($"FAILED ({failures.Count})");
    foreach (var failure in failures) Console.Error.WriteLine($"- {failure}");
    return 1;
}

Console.WriteLine("All parser checks passed.");
return 0;

void Run(string name, Action test)
{
    try
    {
        test();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception ex)
    {
        failures.Add($"{name}: {ex.Message}");
    }
}

async Task RunLiveAsync()
{
    using var service = new QuotaService();
    var snapshot = await service.FetchAsync(CancellationToken.None);
    PrintProvider(snapshot.Codex);
    PrintProvider(snapshot.Claude);
}

async Task PrintDesktopShapeAsync()
{
    try
    {
        var cache = await CredentialReader.ReadClaudeDesktopCacheAsync(CancellationToken.None);
        Console.WriteLine("DESKTOP_CACHE " + Describe(cache));
    }
    catch (Exception ex)
    {
        failures.Add("Desktop credential cache: " + ex.Message);
    }
}

static string Describe(JsonElement element, int depth = 0)
{
    if (depth >= 3) return element.ValueKind.ToString();
    return element.ValueKind switch
    {
        JsonValueKind.Object => "{" + string.Join(",", element.EnumerateObject()
            .Select(property => property.Name + ":" + Describe(property.Value, depth + 1))) + "}",
        JsonValueKind.Array => "[" + string.Join(",", element.EnumerateArray().Take(2)
            .Select(item => Describe(item, depth + 1))) + "]",
        JsonValueKind.String => "String(length=" + (element.GetString()?.Length ?? 0) + ")",
        _ => element.ValueKind.ToString()
    };
}

static void PrintProvider(ProviderQuota quota)
{
    if (!quota.IsAvailable)
    {
        Console.WriteLine($"LIVE {quota.Provider}: {quota.Error}");
        return;
    }

    var windows = string.Join(", ", quota.Windows.Select(window =>
        $"{window.Name} remaining {window.RemainingPercent:0}%"));
    Console.WriteLine($"LIVE {quota.Provider} [{quota.Plan}]: {windows}");
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new Exception($"Expected '{expected}', got '{actual}'.");
    }
}

static void Contains(string expected, string? actual)
{
    if (actual is null || !actual.Contains(expected, StringComparison.Ordinal))
    {
        throw new Exception($"Expected '{actual}' to contain '{expected}'.");
    }
}
