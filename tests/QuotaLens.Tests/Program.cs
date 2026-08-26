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
    Equal(3, quota.Windows.Count);
    Equal("Fable 周额度", quota.Windows[2].Name);
    Equal(46d, quota.Windows[2].RemainingPercent);
    Equal(
        DateTimeOffset.Parse("2026-07-28T16:30:00+00:00"),
        quota.Windows[2].ResetsAt);
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
        ""expiresAt"": 200
      }
    }");

    Equal("newer-token", CredentialReader.FindClaudeDesktopToken(json.RootElement));
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
    Console.WriteLine($"LIVE {quota.Provider}: {windows}");
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
