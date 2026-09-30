using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;
using QuotaLens.Mac.Rendering;
using QuotaLens.Services;

namespace QuotaLens.Mac;

/// <summary>
/// Owns the menu bar item. On macOS a click on a status item always opens its menu (Avalonia
/// raises no click event there), so the menu itself carries the numbers: one line per quota window
/// with the remaining percentage and reset time, followed by the actions. The icon shows the rings.
/// </summary>
internal sealed class QuotaController : IDisposable
{
    private const int MenuTextLimit = 46;

    private readonly Application _app;
    private readonly IClassicDesktopStyleApplicationLifetime _desktop;
    private readonly QuotaService _quotaService = new();
    private readonly DispatcherTimer _refreshTimer = new();
    private readonly HashSet<string> _warningKeys = new(StringComparer.Ordinal);
    private readonly string _settingsPath = Path.Combine(AppPaths.DataDirectory, "settings.json");
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
    private readonly AppSettings _settings;
    private readonly TrayIcon _trayIcon = new() { ToolTipText = "Quota Lens" };
    private readonly NativeMenu _menu = new();
    private PanelWindow? _panel;
    private QuotaSnapshot? _snapshot;
    private CancellationTokenSource? _refreshCancellation;
    private string? _lastStateSummary;

    public QuotaController(Application app, IClassicDesktopStyleApplicationLifetime desktop)
    {
        _app = app;
        _desktop = desktop;
        _settings = LoadSettings();
        _settings.PollSeconds = Math.Clamp(_settings.PollSeconds, 30, 900);
        foreach (var key in _settings.NotifiedLowQuotaKeys ?? new List<string>()) _warningKeys.Add(key);
    }

    public void Start()
    {
        if (_settings.StartWithWindows) TrySetLaunchAtLogin(true);

        _trayIcon.Menu = _menu;
        _trayIcon.Icon = TrayIconRenderer.Render(null, IsDarkMenuBar());
        TrayIcon.SetIcons(_app, new TrayIcons { _trayIcon });
        if (_app.PlatformSettings is { } platformSettings)
        {
            platformSettings.ColorValuesChanged += (_, _) =>
                Dispatcher.UIThread.Post(() => _trayIcon.Icon = TrayIconRenderer.Render(_snapshot, IsDarkMenuBar()));
        }

        BuildMenu();
        _refreshTimer.Interval = TimeSpan.FromSeconds(_settings.PollSeconds);
        _refreshTimer.Tick += async (_, _) => await RefreshAsync(forceClaudeRefresh: false);
        _refreshTimer.Start();
        _ = RefreshAsync(forceClaudeRefresh: false);
    }

    private async Task RefreshAsync(bool forceClaudeRefresh)
    {
        if (_refreshCancellation is not null) return;
        _refreshCancellation = new CancellationTokenSource();
        _panel?.SetRefreshing(true);
        try
        {
            _snapshot = await _quotaService.FetchAsync(_refreshCancellation.Token, forceClaudeRefresh);
            _trayIcon.Icon = TrayIconRenderer.Render(_snapshot, IsDarkMenuBar());
            _trayIcon.ToolTipText =
                $"Quota Lens · Codex {PrimaryRemaining(_snapshot.Codex)} · Claude {PrimaryRemaining(_snapshot.Claude)}";
            BuildMenu();
            if (_panel?.IsVisible == true) _panel.Render(_snapshot);
            NotifyLowQuota(_snapshot);
            LogStateChange(_snapshot);
        }
        catch (OperationCanceledException)
        {
            // The app is exiting.
        }
        catch (Exception ex)
        {
            StartupLog.Write($"refresh failed: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _refreshCancellation.Dispose();
            _refreshCancellation = null;
            _panel?.SetRefreshing(false);
        }
    }

    private void BuildMenu()
    {
        _menu.Items.Clear();
        if (_snapshot is null)
        {
            _menu.Items.Add(new NativeMenuItem("正在获取额度…") { IsEnabled = false });
        }
        else
        {
            var now = DateTimeOffset.Now;
            AddProviderLines(_snapshot.Codex, now);
            AddProviderLines(_snapshot.Claude, now);
            _menu.Items.Add(new NativeMenuItemSeparator());
            _menu.Items.Add(new NativeMenuItem($"更新于 {_snapshot.FetchedAt:HH:mm:ss}") { IsEnabled = false });
        }

        _menu.Items.Add(Action("打开面板", ShowPanel));
        _menu.Items.Add(Action("立即刷新", () => _ = RefreshAsync(forceClaudeRefresh: true)));
        _menu.Items.Add(Action("息屏并保持运行", MacPlatform.TurnOffDisplaysKeepingAwake));
        _menu.Items.Add(new NativeMenuItemSeparator());
        _menu.Items.Add(Toggle("登录时启动", _settings.StartWithWindows, () =>
        {
            if (TrySetLaunchAtLogin(!_settings.StartWithWindows))
            {
                _settings.StartWithWindows = !_settings.StartWithWindows;
                SaveSettings();
            }
            BuildMenu();
        }));
        _menu.Items.Add(Toggle("低额度提醒", _settings.LowQuotaNotificationsEnabled, () =>
        {
            _settings.LowQuotaNotificationsEnabled = !_settings.LowQuotaNotificationsEnabled;
            SaveSettings();
            BuildMenu();
        }));
        _menu.Items.Add(new NativeMenuItemSeparator());
        _menu.Items.Add(new NativeMenuItem($"Quota Lens {AppPaths.Version}") { IsEnabled = false });
        _menu.Items.Add(Action("退出 Quota Lens", () => _desktop.Shutdown()));
    }

    /// <summary>
    /// "Claude Code · Max 20×" followed by "5 小时  85% · 03:40 重置" lines. The lines stay enabled
    /// (grey disabled items are hard to read) and open the panel when chosen.
    /// </summary>
    private void AddProviderLines(ProviderQuota quota, DateTimeOffset now)
    {
        var title = quota.IsAvailable ? $"{quota.Provider} · {quota.Plan}" : quota.Provider;
        _menu.Items.Add(Action(title, ShowPanel));
        if (!quota.IsAvailable)
        {
            _menu.Items.Add(Action("    " + Truncate(quota.Error ?? "未连接"), ShowPanel));
            return;
        }

        foreach (var window in quota.Windows)
        {
            var reset = QuotaWindowLegend.CompactReset(window.ResetsAt, now);
            var suffix = window.ResetsAt is null ? string.Empty : $" · {reset} 重置";
            _menu.Items.Add(Action($"    {window.Name}  {window.RemainingPercent:0}%{suffix}", ShowPanel));
        }
    }

    private void ShowPanel()
    {
        if (_panel is null)
        {
            _panel = new PanelWindow();
            _panel.RefreshRequested += (_, _) => _ = RefreshAsync(forceClaudeRefresh: true);
        }

        if (_snapshot is not null) _panel.Render(_snapshot);
        _panel.ShowUnderMenuBar();
    }

    private void NotifyLowQuota(QuotaSnapshot snapshot)
    {
        if (!_settings.LowQuotaNotificationsEnabled) return;
        var batch = LowQuotaAlertService.Scan(new[] { snapshot.Codex, snapshot.Claude }, _warningKeys);
        if (batch.StateChanged)
        {
            _settings.NotifiedLowQuotaKeys = _warningKeys.TakeLast(128).ToList();
            SaveSettings();
        }

        if (batch.Alerts.Count == 0) return;
        var lines = batch.Alerts.Take(3).Select(alert =>
            $"{alert.Provider} {alert.WindowName}剩余 {alert.RemainingPercent:0}%");
        MacPlatform.Notify("Quota Lens · 低额度提醒", string.Join("；", lines));
    }

    /// <summary>Logs availability changes only, so the log shows what happened without per-minute noise.</summary>
    private void LogStateChange(QuotaSnapshot snapshot)
    {
        static string Describe(ProviderQuota quota) => quota.IsAvailable
            ? $"ok({quota.Windows.Count})"
            : $"error: {quota.Error}";

        var summary = $"refresh: codex {Describe(snapshot.Codex)}; claude {Describe(snapshot.Claude)}";
        if (summary == _lastStateSummary) return;
        _lastStateSummary = summary;
        StartupLog.Write(summary);
    }

    private bool TrySetLaunchAtLogin(bool enabled)
    {
        try
        {
            if (OperatingSystem.IsMacOS()) MacPlatform.SetLaunchAtLogin(enabled);
            return true;
        }
        catch (Exception ex)
        {
            StartupLog.Write($"autostart failed: {ex.GetType().Name}: {ex.Message}");
            MacPlatform.Notify("Quota Lens", $"无法更新登录时启动：{ex.Message}");
            return false;
        }
    }

    private bool IsDarkMenuBar() =>
        _app.PlatformSettings?.GetColorValues().ThemeVariant != PlatformThemeVariant.Light;

    private AppSettings LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_settingsPath)) ?? NewSettings();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            StartupLog.Write($"settings unreadable, using defaults: {ex.Message}");
        }

        return NewSettings();
    }

    /// <summary>First run on macOS: launch at login on, like a typical menu bar utility.</summary>
    private static AppSettings NewSettings() => new() { StartWithWindows = true };

    private void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(_settings, _jsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StartupLog.Write($"settings not saved: {ex.Message}");
        }
    }

    private static string PrimaryRemaining(ProviderQuota quota) =>
        quota.IsAvailable ? $"{quota.Windows[0].RemainingPercent:0}%" : "未连接";

    private static string Truncate(string text) =>
        text.Length <= MenuTextLimit ? text : text[..(MenuTextLimit - 1)] + "…";

    private static NativeMenuItem Action(string header, Action onClick)
    {
        var item = new NativeMenuItem(header);
        item.Click += (_, _) => onClick();
        return item;
    }

    private static NativeMenuItem Toggle(string header, bool isChecked, Action onClick)
    {
        var item = new NativeMenuItem(header)
        {
            ToggleType = NativeMenuItemToggleType.CheckBox,
            IsChecked = isChecked
        };
        item.Click += (_, _) => onClick();
        return item;
    }

    public void Dispose()
    {
        _refreshTimer.Stop();
        _refreshCancellation?.Cancel();
        MacPlatform.StopKeepingAwake();
        _trayIcon.IsVisible = false;
        _trayIcon.Dispose();
        _quotaService.Dispose();
    }
}
