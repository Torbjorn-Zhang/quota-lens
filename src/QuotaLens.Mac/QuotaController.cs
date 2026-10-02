using System.Text.Json;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;
using QuotaLens.Mac.Rendering;
using QuotaLens.Services;

namespace QuotaLens.Mac;

/// <summary>
/// Owns the menu bar item and the panel. The item shows the ring icon; a click toggles the panel,
/// which also carries the settings and actions (launch at login, alerts, display off, quit).
/// </summary>
internal sealed class QuotaController : IDisposable
{
    private static readonly TimeSpan ReopenGuard = TimeSpan.FromMilliseconds(350);

    private readonly Application _app;
    private readonly IClassicDesktopStyleApplicationLifetime _desktop;
    private readonly QuotaService _quotaService = new();
    private readonly DispatcherTimer _refreshTimer = new();
    private readonly HashSet<string> _warningKeys = new(StringComparer.Ordinal);
    private readonly string _settingsPath = Path.Combine(AppPaths.DataDirectory, "settings.json");
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
    private readonly AppSettings _settings;
    private readonly PanelWindow _panel = new();
    private readonly DispatcherTimer _outsideClickTimer = new() { Interval = TimeSpan.FromMilliseconds(60) };
    private readonly DispatcherTimer _thumbnailTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private MacStatusItem? _statusItem;
    private QuotaSnapshot? _snapshot;
    private CancellationTokenSource? _refreshCancellation;
    private string? _lastStateSummary;
    private double _unitsPerPoint = 1;
    private bool _mouseWasDown;

    public QuotaController(Application app, IClassicDesktopStyleApplicationLifetime desktop)
    {
        _app = app;
        _desktop = desktop;
        _settings = LoadSettings();
        _settings.PollSeconds = Math.Clamp(_settings.PollSeconds, 30, 900);
        foreach (var key in _settings.NotifiedLowQuotaKeys ?? new List<string>()) _warningKeys.Add(key);

        var view = _panel.Panel;
        view.RefreshRequested += (_, _) => _ = RefreshAsync(forceClaudeRefresh: true);
        view.ScreenOffRequested += (_, _) =>
        {
            _panel.Hide();
            MacPlatform.TurnOffDisplaysKeepingAwake();
        };
        view.QuitRequested += (_, _) => _desktop.Shutdown();
        view.LaunchAtLoginToggled += (_, _) =>
        {
            if (TrySetLaunchAtLogin(!_settings.StartWithWindows))
            {
                _settings.StartWithWindows = !_settings.StartWithWindows;
                SaveSettings();
            }
            SyncPanelOptions();
        };
        view.AlertsToggled += (_, _) =>
        {
            _settings.LowQuotaNotificationsEnabled = !_settings.LowQuotaNotificationsEnabled;
            SaveSettings();
            SyncPanelOptions();
        };
        SyncPanelOptions();
        _outsideClickTimer.Tick += (_, _) => HidePanelOnOutsideClick();
    }

    public void Start()
    {
        if (_settings.StartWithWindows) TrySetLaunchAtLogin(true);

        if (OperatingSystem.IsMacOS())
        {
            _statusItem = new MacStatusItem(() => Dispatcher.UIThread.Post(TogglePanel));
            UpdateStatusItem();
        }

        if (_app.PlatformSettings is { } platformSettings)
        {
            platformSettings.ColorValuesChanged += (_, _) => Dispatcher.UIThread.Post(UpdateStatusItem);
        }

        _refreshTimer.Interval = TimeSpan.FromSeconds(_settings.PollSeconds);
        _refreshTimer.Tick += async (_, _) => await RefreshAsync(forceClaudeRefresh: false);
        _refreshTimer.Start();
        _ = RefreshAsync(forceClaudeRefresh: false);

        // The thumbnail countdown is minute-precise; redraw it between data refreshes.
        _thumbnailTimer.Tick += (_, _) => UpdateStatusItem();
        _thumbnailTimer.Start();

        if (Program.SelfTest) _ = RunSelfTestAsync();
    }

    private async Task RefreshAsync(bool forceClaudeRefresh)
    {
        if (_refreshCancellation is not null) return;
        _refreshCancellation = new CancellationTokenSource();
        _panel.Panel.SetRefreshing(true);
        try
        {
            _snapshot = await _quotaService.FetchAsync(_refreshCancellation.Token, forceClaudeRefresh);
            UpdateStatusItem();
            _panel.Panel.Render(_snapshot);
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
            _panel.Panel.SetRefreshing(false);
        }
    }

    private void UpdateStatusItem()
    {
        if (_statusItem is null || !OperatingSystem.IsMacOS()) return;
        _statusItem.SetImage(
            TrayIconRenderer.RenderPng(_snapshot, IsDarkMenuBar(), DateTimeOffset.Now),
            TrayIconRenderer.PointWidth,
            TrayIconRenderer.PointHeight);
        _statusItem.SetToolTip(_snapshot is null
            ? "Quota Lens · 正在获取额度"
            : $"Quota Lens · Codex {PrimaryRemaining(_snapshot.Codex)} · Claude {PrimaryRemaining(_snapshot.Claude)}");
    }

    private void TogglePanel()
    {
        if (_panel.IsVisible)
        {
            _panel.Hide();
            return;
        }

        // Clicking the icon while the panel is open first deactivates (and hides) the panel;
        // that same click must not immediately reopen it.
        if (DateTime.UtcNow - _panel.HiddenAtUtc < ReopenGuard) return;
        ShowPanel();
    }

    private void ShowPanel()
    {
        if (_snapshot is not null) _panel.Panel.Render(_snapshot);
        SyncPanelOptions();

        double? anchorCenterX = null;
        var unitsPerDip = 1.0;
        if (OperatingSystem.IsMacOS() && _statusItem is not null)
        {
            // Avalonia's position units on macOS relate to points by the ratio of its primary
            // screen width to AppKit's; measuring it avoids assuming points or pixels.
            var cocoaScreen = MacStatusItem.PrimaryScreenFrame();
            var avaloniaScreen = _panel.Screens.Primary;
            if (avaloniaScreen is not null && cocoaScreen.Size.Width > 0)
            {
                unitsPerDip = avaloniaScreen.Bounds.Width / cocoaScreen.Size.Width;
                var item = _statusItem.ScreenFrame;
                if (item.Size.Width > 0)
                {
                    anchorCenterX = avaloniaScreen.Bounds.X
                                    + (item.Origin.X - cocoaScreen.Origin.X + item.Size.Width / 2) * unitsPerDip;
                }
            }

            _unitsPerPoint = unitsPerDip;
            MacStatusItem.ActivateApp();
        }

        _panel.ShowBelowMenuBar(anchorCenterX, unitsPerDip);
        _mouseWasDown = true; // ignore the press that opened the panel
        _outsideClickTimer.Start();
    }

    /// <summary>
    /// A borderless panel of an accessory app does not reliably become the key window, so its
    /// Deactivated event cannot be trusted to close it. Instead, while the panel is open, a new mouse
    /// press anywhere outside the panel and the menu bar icon closes it. Reading the pressed buttons
    /// and cursor position needs no extra permission.
    /// </summary>
    private void HidePanelOnOutsideClick()
    {
        if (!_panel.IsVisible || _statusItem is null || !OperatingSystem.IsMacOS())
        {
            _outsideClickTimer.Stop();
            return;
        }

        var down = MacStatusItem.PressedMouseButtons() != 0;
        var pressed = down && !_mouseWasDown;
        _mouseWasDown = down;
        if (!pressed) return;

        var screen = MacStatusItem.PrimaryScreenFrame();
        var cursor = MacStatusItem.MouseLocation();
        var item = _statusItem.ScreenFrame;
        var insideItem = cursor.X >= item.Origin.X && cursor.X <= item.Origin.X + item.Size.Width
                         && cursor.Y >= item.Origin.Y && cursor.Y <= item.Origin.Y + item.Size.Height;
        if (insideItem) return; // the icon's own click toggles the panel

        // Cocoa points (origin bottom left) to Avalonia position units (origin top left).
        var origin = _panel.Screens.Primary?.Bounds.Position ?? default;
        var x = origin.X + (cursor.X - screen.Origin.X) * _unitsPerPoint;
        var y = origin.Y + (screen.Origin.Y + screen.Size.Height - cursor.Y) * _unitsPerPoint;
        var panel = new Rect(
            _panel.Position.X,
            _panel.Position.Y,
            _panel.Bounds.Width * _unitsPerPoint,
            _panel.Bounds.Height * _unitsPerPoint);
        if (!panel.Contains(new Point(x, y))) _panel.Hide();
    }

    /// <summary>
    /// <c>--self-test</c>: logs the menu bar item's real size and exercises the click path twice
    /// (open, then close), so the behaviour can be checked from the log without a screen.
    /// </summary>
    private async Task RunSelfTestAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(3));
        if (_statusItem is null || !OperatingSystem.IsMacOS())
        {
            StartupLog.Write("self-test: no status item on this platform");
            return;
        }

        var frame = _statusItem.ScreenFrame;
        StartupLog.Write(
            $"self-test: status item {frame.Size.Width:0.#}x{frame.Size.Height:0.#} pt at x={frame.Origin.X:0.#}; " +
            $"icon {TrayIconRenderer.PointWidth:0.#}x{TrayIconRenderer.PointHeight:0.#} pt");

        // Wait for real data so the panel is checked the way the user sees it.
        for (var waited = 0; _snapshot is null && waited < 90; waited++)
        {
            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        _statusItem.PerformClick();
        await Task.Delay(TimeSpan.FromSeconds(2));
        var screen = _panel.Screens.Primary;
        StartupLog.Write(
            $"self-test: after click panel visible={_panel.IsVisible} active={_panel.IsActive} " +
            $"position={_panel.Position} size={_panel.Bounds.Width:0}x{_panel.Bounds.Height:0} " +
            $"screen bounds={screen?.Bounds} work={screen?.WorkingArea} scaling={screen?.Scaling}");
        StartupLog.Write($"self-test: countdowns shown: {string.Join(" | ", _panel.Panel.CountdownTexts())}");
        SaveSelfTestSnapshot();
        var thumbnail = Path.Combine(AppPaths.DataDirectory, "self-test-menubar.png");
        File.WriteAllBytes(thumbnail, TrayIconRenderer.RenderPng(_snapshot, IsDarkMenuBar(), DateTimeOffset.Now));
        StartupLog.Write($"self-test: menu bar thumbnail rendered to {thumbnail} (dark={IsDarkMenuBar()})");

        await Task.Delay(TimeSpan.FromSeconds(1));
        _statusItem.PerformClick();
        await Task.Delay(TimeSpan.FromSeconds(1));
        StartupLog.Write(
            $"self-test: after second click panel visible={_panel.IsVisible}, outside-click watcher " +
            $"{(_outsideClickTimer.IsEnabled ? "running" : "stopped")}");
    }

    /// <summary>Renders the live panel as displayed (the app drawing its own window needs no screen-recording permission).</summary>
    private void SaveSelfTestSnapshot()
    {
        try
        {
            var view = _panel.Panel;
            var size = view.Bounds.Size;
            using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(
                new PixelSize((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height)),
                new Vector(96, 96));
            bitmap.Render(view);
            var path = Path.Combine(AppPaths.DataDirectory, "self-test-panel.png");
            bitmap.Save(path);
            StartupLog.Write($"self-test: panel rendered to {path}");
        }
        catch (Exception ex)
        {
            StartupLog.Write($"self-test: panel render failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void SyncPanelOptions() =>
        _panel.Panel.SetOptions(_settings.StartWithWindows, _settings.LowQuotaNotificationsEnabled);

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

    public void Dispose()
    {
        _thumbnailTimer.Stop();
        _outsideClickTimer.Stop();
        _refreshTimer.Stop();
        _refreshCancellation?.Cancel();
        MacPlatform.StopKeepingAwake();
        if (OperatingSystem.IsMacOS()) _statusItem?.Dispose();
        _quotaService.Dispose();
    }
}
