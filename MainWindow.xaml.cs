using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using QuotaLens.Services;
using DrawingIcon = System.Drawing.Icon;
using DrawingPen = System.Drawing.Pen;
using DrawingPixelFormat = System.Drawing.Imaging.PixelFormat;
using Forms = System.Windows.Forms;
using WpfProgressBar = System.Windows.Controls.ProgressBar;

namespace QuotaLens;

public partial class MainWindow : Window
{
    private readonly QuotaService _quotaService = new();
    private readonly SettingsService _settingsService = new();
    private readonly DispatcherTimer _refreshTimer = new();
    private readonly DispatcherTimer _countdownTimer = new();
    private readonly HashSet<string> _warningKeys = new(StringComparer.Ordinal);
    private readonly AppSettings _settings;
    private Forms.NotifyIcon? _trayIcon;
    private DrawingIcon? _trayIconImage;
    private Forms.ToolStripMenuItem? _pinMenuItem;
    private Forms.ToolStripMenuItem? _autoStartMenuItem;
    private Forms.ToolStripMenuItem? _lowQuotaNotificationsMenuItem;
    private readonly List<Forms.ToolStripMenuItem> _opacityMenuItems = new();
    private QuotaSnapshot? _snapshot;
    private CancellationTokenSource? _refreshCancellation;
    private bool _allowClose;
    private bool _settingsLoaded;
    private bool _changingStartup;
    private bool _hasShownTrayTip;

    public MainWindow()
    {
        InitializeComponent();
        _settings = _settingsService.Load();
        _settings.PollSeconds = Math.Clamp(_settings.PollSeconds, 30, 900);
        _settings.WidgetOpacity = Math.Clamp(_settings.WidgetOpacity, 0.55, 0.96);
        _settings.NotifiedLowQuotaKeys ??= new List<string>();
        foreach (var key in _settings.NotifiedLowQuotaKeys)
        {
            _warningKeys.Add(key);
        }

        if (GlassFrame.Background is Freezable freezable)
        {
            GlassFrame.Background = (System.Windows.Media.Brush)freezable.CloneCurrentValue();
        }
        ApplyGlassOpacity(_settings.WidgetOpacity);
        Topmost = _settings.AlwaysOnTop;
        UpdatePinVisual();

        StartWithWindowsCheckBox.IsChecked = _settings.StartWithWindows;
        _settingsLoaded = true;

        ConfigureTrayIcon();
        if (_settings.StartWithWindows)
        {
            SetStartWithWindows(enabled: true, showError: false);
        }
        _refreshTimer.Interval = TimeSpan.FromSeconds(_settings.PollSeconds);
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();
        _refreshTimer.Start();

        _countdownTimer.Interval = TimeSpan.FromSeconds(1);
        _countdownTimer.Tick += (_, _) => UpdateCountdowns();
        _countdownTimer.Start();

        Loaded += async (_, _) =>
        {
            RestoreWindowPosition();
            await RefreshAsync();
        };
    }

    public void InitializeInTray()
    {
        Hide();
        _ = RefreshAsync();
    }

    private void ConfigureTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("打开 Quota Lens", null, (_, _) => ShowWindow());
        menu.Items.Add("立即刷新", null, async (_, _) => await RefreshAsync());
        menu.Items.Add("息屏并持续保持运行", null, (_, _) =>
            Dispatcher.BeginInvoke(new Action(() => _ = TurnOffScreenAsync())));
        _pinMenuItem = new Forms.ToolStripMenuItem("窗口置顶")
        {
            Checked = _settings.AlwaysOnTop
        };
        _pinMenuItem.Click += (_, _) => Dispatcher.Invoke(ToggleAlwaysOnTop);
        menu.Items.Add(_pinMenuItem);

        var opacityMenu = new Forms.ToolStripMenuItem("透明度");
        foreach (var option in new[] { ("轻透 65%", 0.65), ("默认 74%", 0.74), ("清晰 88%", 0.88) })
        {
            var item = new Forms.ToolStripMenuItem(option.Item1) { Tag = option.Item2 };
            item.Click += (_, _) => Dispatcher.Invoke(() => SetGlassOpacity((double)item.Tag));
            _opacityMenuItems.Add(item);
            opacityMenu.DropDownItems.Add(item);
        }
        UpdateOpacityMenu();
        menu.Items.Add(opacityMenu);

        _lowQuotaNotificationsMenuItem = new Forms.ToolStripMenuItem("低额度提醒")
        {
            Checked = _settings.LowQuotaNotificationsEnabled
        };
        _lowQuotaNotificationsMenuItem.Click += (_, _) => Dispatcher.Invoke(() =>
            SetLowQuotaNotifications(!_settings.LowQuotaNotificationsEnabled));
        menu.Items.Add(_lowQuotaNotificationsMenuItem);

        _autoStartMenuItem = new Forms.ToolStripMenuItem("开机启动")
        {
            Checked = _settings.StartWithWindows
        };
        _autoStartMenuItem.Click += (_, _) => Dispatcher.Invoke(() =>
            SetStartWithWindows(!_settings.StartWithWindows, showError: true));
        menu.Items.Add(_autoStartMenuItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitApplication());

        _trayIconImage = CreateTrayIcon();
        _trayIcon = new Forms.NotifyIcon
        {
            Icon = _trayIconImage,
            Text = "Quota Lens · 等待刷新",
            Visible = true,
            ContextMenuStrip = menu
        };
        _trayIcon.DoubleClick += (_, _) => ShowWindow();
    }

    private static DrawingIcon CreateTrayIcon()
    {
        using var bitmap = new Bitmap(32, 32, DrawingPixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(System.Drawing.Color.Transparent);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

            using var background = new SolidBrush(System.Drawing.Color.FromArgb(255, 27, 36, 55));
            graphics.FillEllipse(background, 2.5f, 2.5f, 27f, 27f);

            using var ring = new DrawingPen(System.Drawing.Color.FromArgb(255, 67, 220, 190), 3.2f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            graphics.DrawArc(ring, 6f, 6f, 20f, 20f, 135f, 275f);

            using var remainder = new DrawingPen(System.Drawing.Color.FromArgb(255, 91, 108, 139), 2.2f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            graphics.DrawArc(remainder, 6.5f, 6.5f, 19f, 19f, 55f, 55f);

            using var needle = new DrawingPen(System.Drawing.Color.White, 2.2f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            graphics.DrawLine(needle, 16f, 16f, 21.5f, 11f);

            using var hub = new SolidBrush(System.Drawing.Color.White);
            graphics.FillEllipse(hub, 13.7f, 13.7f, 4.6f, 4.6f);
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var borrowed = DrawingIcon.FromHandle(handle);
            return (DrawingIcon)borrowed.Clone();
        }
        finally
        {
            NativeIcon.DestroyIcon(handle);
        }
    }

    private async Task RefreshAsync()
    {
        if (_refreshCancellation is not null) return;

        _refreshCancellation = new CancellationTokenSource();
        RefreshButton.IsEnabled = false;
        RefreshButton.Opacity = 0.45;
        try
        {
            _snapshot = await _quotaService.FetchAsync(_refreshCancellation.Token);
            RenderProvider(
                _snapshot.Codex,
                CodexPlanText,
                CodexStatusDot,
                CodexContentPanel,
                CodexErrorText,
                CodexExtraText,
                CodexPrimaryName,
                CodexPrimaryValue,
                CodexPrimaryBar,
                CodexPrimaryReset,
                CodexSecondaryName,
                CodexSecondaryValue,
                CodexSecondaryBar,
                CodexSecondaryReset);
            RenderProvider(
                _snapshot.Claude,
                ClaudePlanText,
                ClaudeStatusDot,
                ClaudeContentPanel,
                ClaudeErrorText,
                ClaudeExtraText,
                ClaudePrimaryName,
                ClaudePrimaryValue,
                ClaudePrimaryBar,
                ClaudePrimaryReset,
                ClaudeSecondaryName,
                ClaudeSecondaryValue,
                ClaudeSecondaryBar,
                ClaudeSecondaryReset);
            RenderWindow(
                _snapshot.Claude.IsAvailable
                    ? _snapshot.Claude.Windows.ElementAtOrDefault(2)
                    : null,
                ClaudeScopedName,
                ClaudeScopedValue,
                ClaudeScopedBar,
                ClaudeScopedReset);

            UpdatedText.Text = $"更新 {_snapshot.FetchedAt:HH:mm:ss} · {_settings.PollSeconds}s";
            UpdateTrayText();
            NotifyLowQuota(_snapshot.Codex, _snapshot.Claude);
        }
        catch (OperationCanceledException)
        {
            // Normal when the application exits during an in-flight refresh.
        }
        finally
        {
            _refreshCancellation.Dispose();
            _refreshCancellation = null;
            RefreshButton.IsEnabled = true;
            RefreshButton.Opacity = 1;
        }
    }

    private static void RenderProvider(
        ProviderQuota quota,
        TextBlock planText,
        Ellipse statusDot,
        FrameworkElement contentPanel,
        TextBlock errorText,
        TextBlock extraText,
        TextBlock primaryName,
        TextBlock primaryValue,
        WpfProgressBar primaryBar,
        TextBlock primaryReset,
        TextBlock secondaryName,
        TextBlock secondaryValue,
        WpfProgressBar secondaryBar,
        TextBlock secondaryReset)
    {
        if (!quota.IsAvailable)
        {
            planText.Text = "未连接";
            statusDot.Fill = Brush("#FF6B7A");
            contentPanel.Visibility = Visibility.Collapsed;
            extraText.Visibility = Visibility.Collapsed;
            errorText.Text = quota.Error;
            errorText.Visibility = Visibility.Visible;
            return;
        }

        planText.Text = quota.Plan;
        statusDot.Fill = Brush("#38D6A3");
        contentPanel.Visibility = Visibility.Visible;
        errorText.Visibility = Visibility.Collapsed;
        extraText.Text = quota.ExtraInfo;
        extraText.Visibility = string.IsNullOrWhiteSpace(quota.ExtraInfo)
            ? Visibility.Collapsed
            : Visibility.Visible;

        RenderWindow(quota.Windows.ElementAtOrDefault(0), primaryName, primaryValue, primaryBar, primaryReset);
        RenderWindow(quota.Windows.ElementAtOrDefault(1), secondaryName, secondaryValue, secondaryBar, secondaryReset);
        SetWindowColumns(primaryName, secondaryName, quota.Windows.Count);
    }

    private static void SetWindowColumns(TextBlock primaryName, TextBlock secondaryName, int windowCount)
    {
        var primary = (primaryName.Parent as Grid)?.Parent as StackPanel;
        var secondary = (secondaryName.Parent as Grid)?.Parent as StackPanel;
        if (primary is not null) Grid.SetColumnSpan(primary, windowCount == 1 ? 3 : 1);
        if (secondary is not null) Grid.SetColumnSpan(secondary, 1);
    }

    private static void RenderWindow(
        QuotaWindow? window,
        TextBlock name,
        TextBlock value,
        WpfProgressBar bar,
        TextBlock reset)
    {
        var panel = name.Parent as Grid;
        var container = panel?.Parent as StackPanel;
        if (window is null)
        {
            if (container is not null) container.Visibility = Visibility.Collapsed;
            return;
        }

        if (container is not null) container.Visibility = Visibility.Visible;
        name.Text = window.Name;
        value.Text = $"{window.RemainingPercent:0}%";
        bar.Value = window.RemainingPercent;
        bar.Foreground = RemainingBrush(window.RemainingPercent);
        reset.Tag = window;
        reset.Text = FormatReset(window.ResetsAt);
    }

    private void UpdateCountdowns()
    {
        foreach (var text in new[]
                 {
                     CodexPrimaryReset, CodexSecondaryReset,
                     ClaudePrimaryReset, ClaudeSecondaryReset, ClaudeScopedReset
                 })
        {
            if (text.Tag is QuotaWindow window)
            {
                text.Text = FormatReset(window.ResetsAt);
            }
        }
    }

    private void UpdateTrayText()
    {
        if (_trayIcon is null || _snapshot is null) return;
        var codex = PrimaryRemaining(_snapshot.Codex);
        var claude = PrimaryRemaining(_snapshot.Claude);
        var text = $"Quota Lens · Codex {codex} · Claude {claude}";
        _trayIcon.Text = text.Length <= 63 ? text : text[..63];
    }

    private void NotifyLowQuota(params ProviderQuota[] providers)
    {
        if (_trayIcon is null || !_settings.LowQuotaNotificationsEnabled) return;

        var batch = LowQuotaAlertService.Scan(providers, _warningKeys);
        if (batch.StateChanged)
        {
            _settings.NotifiedLowQuotaKeys = _warningKeys.TakeLast(128).ToList();
            _settingsService.Save(_settings);
        }

        if (batch.Alerts.Count == 0) return;
        var lines = batch.Alerts.Take(3).Select(alert =>
            $"{alert.Provider} {alert.WindowName}剩余 {alert.RemainingPercent:0}%");
        var suffix = batch.Alerts.Count > 3 ? $"\n另有 {batch.Alerts.Count - 3} 项" : string.Empty;

        _trayIcon.BalloonTipTitle = "低额度提醒";
        _trayIcon.BalloonTipText = string.Join("\n", lines) + suffix;
        _trayIcon.BalloonTipIcon = Forms.ToolTipIcon.Warning;
        _trayIcon.ShowBalloonTip(5000);
    }

    private void SetLowQuotaNotifications(bool enabled)
    {
        _settings.LowQuotaNotificationsEnabled = enabled;
        if (_lowQuotaNotificationsMenuItem is not null)
        {
            _lowQuotaNotificationsMenuItem.Checked = enabled;
        }
        _settingsService.Save(_settings);
    }

    private static string PrimaryRemaining(ProviderQuota quota) =>
        quota.IsAvailable ? $"{quota.Windows[0].RemainingPercent:0}%" : "未连接";

    private static string FormatReset(DateTimeOffset? resetAt)
    {
        if (resetAt is null) return "重置时间未知";
        var remaining = resetAt.Value - DateTimeOffset.Now;
        if (remaining <= TimeSpan.Zero) return "额度窗口正在重置";

        var countdown = remaining.TotalDays >= 1
            ? $"{(int)remaining.TotalDays}天{remaining.Hours}时"
            : remaining.TotalHours >= 1
                ? $"{(int)remaining.TotalHours}时{remaining.Minutes}分"
                : $"{Math.Max(0, remaining.Minutes)}分{Math.Max(0, remaining.Seconds)}秒";
        return $"{countdown}后 · {resetAt.Value.LocalDateTime:M/d HH:mm}";
    }

    private static SolidColorBrush RemainingBrush(double remaining) =>
        remaining switch
        {
            <= 20 => Brush("#FF6B7A"),
            <= 40 => Brush("#FFB454"),
            _ => Brush("#38D6A3")
        };

    private static SolidColorBrush Brush(string color) =>
        new((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private void CloseButton_Click(object sender, RoutedEventArgs e) => HideToTray();

    private async void ScreenOffButton_Click(object sender, RoutedEventArgs e) =>
        await TurnOffScreenAsync();

    private void PinButton_Click(object sender, RoutedEventArgs e) => ToggleAlwaysOnTop();

    private async Task TurnOffScreenAsync()
    {
        if (!ScreenOffButton.IsEnabled) return;

        ScreenOffButton.IsEnabled = false;
        StartKeepingAwake();
        UpdatedText.Text = "即将息屏 · 持续保持运行";

        // Let the click release finish so it does not immediately wake the monitor again.
        await Task.Delay(900);
        ScreenOffButton.IsEnabled = true;
        NativePower.TurnOffDisplays();
    }

    private void StartKeepingAwake()
    {
        NativePower.SetThreadExecutionState(
            NativePower.ExecutionState.Continuous | NativePower.ExecutionState.SystemRequired);
    }

    private void StopKeepingAwake()
    {
        NativePower.SetThreadExecutionState(NativePower.ExecutionState.Continuous);
    }

    private void WidgetHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        try
        {
            DragMove();
            SaveWindowState();
        }
        catch (InvalidOperationException)
        {
            // The mouse button may be released before DragMove starts.
        }
    }

    private void Window_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e) =>
        ApplyGlassOpacity(Math.Min(0.96, _settings.WidgetOpacity + 0.10));

    private void Window_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e) =>
        ApplyGlassOpacity(_settings.WidgetOpacity);

    private void StartWithWindows_Changed(object sender, RoutedEventArgs e)
    {
        if (!_settingsLoaded || _changingStartup) return;
        SetStartWithWindows(StartWithWindowsCheckBox.IsChecked == true, showError: true);
    }

    private void SetStartWithWindows(bool enabled, bool showError)
    {
        try
        {
            _settingsService.SetStartWithWindows(enabled);
            _settings.StartWithWindows = enabled;
            _changingStartup = true;
            StartWithWindowsCheckBox.IsChecked = enabled;
            _changingStartup = false;
            if (_autoStartMenuItem is not null) _autoStartMenuItem.Checked = enabled;
            _settingsService.Save(_settings);
        }
        catch (Exception ex)
        {
            _changingStartup = true;
            StartWithWindowsCheckBox.IsChecked = _settings.StartWithWindows;
            _changingStartup = false;
            if (_autoStartMenuItem is not null) _autoStartMenuItem.Checked = _settings.StartWithWindows;
            if (showError)
            {
                System.Windows.MessageBox.Show(this, $"无法更新开机启动设置：{ex.Message}", "Quota Lens",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private void ToggleAlwaysOnTop()
    {
        Topmost = !Topmost;
        _settings.AlwaysOnTop = Topmost;
        UpdatePinVisual();
        _settingsService.Save(_settings);
    }

    private void UpdatePinVisual()
    {
        PinButton.Foreground = Topmost ? Brush("#7C8CFF") : Brush("#CDE0EDFF");
        PinButton.Background = Topmost ? Brush("#287C8CFF") : Brush("#14FFFFFF");
        PinButton.Content = Topmost ? "◆" : "◇";
        PinButton.ToolTip = Topmost ? "取消置顶" : "置顶";
        if (_pinMenuItem is not null) _pinMenuItem.Checked = Topmost;
    }

    private void SetGlassOpacity(double opacity)
    {
        _settings.WidgetOpacity = Math.Clamp(opacity, 0.55, 0.96);
        ApplyGlassOpacity(_settings.WidgetOpacity);
        UpdateOpacityMenu();
        _settingsService.Save(_settings);
    }

    private void ApplyGlassOpacity(double opacity)
    {
        if (GlassFrame.Background is System.Windows.Media.Brush brush) brush.Opacity = opacity;
    }

    private void UpdateOpacityMenu()
    {
        foreach (var item in _opacityMenuItems)
        {
            item.Checked = item.Tag is double value && Math.Abs(value - _settings.WidgetOpacity) < 0.01;
        }
    }

    private void RestoreWindowPosition()
    {
        if (_settings.WindowLeft is not double left || _settings.WindowTop is not double top) return;
        var visible = left + Width > SystemParameters.VirtualScreenLeft
                      && left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth
                      && top + Height > SystemParameters.VirtualScreenTop
                      && top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight;
        if (!visible) return;
        Left = left;
        Top = top;
    }

    private void SaveWindowState()
    {
        if (!IsLoaded || WindowState != WindowState.Normal) return;
        _settings.WindowLeft = Left;
        _settings.WindowTop = Top;
        _settings.AlwaysOnTop = Topmost;
        _settingsService.Save(_settings);
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (WindowState == WindowState.Minimized)
        {
            HideToTray();
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }
        base.OnClosing(e);
    }

    private void HideToTray()
    {
        SaveWindowState();
        Hide();
        WindowState = WindowState.Normal;
        if (_trayIcon is not null && !_hasShownTrayTip)
        {
            _trayIcon.BalloonTipTitle = "Quota Lens 仍在运行";
            _trayIcon.BalloonTipText = "双击托盘图标可重新打开。";
            _trayIcon.BalloonTipIcon = Forms.ToolTipIcon.Info;
            _trayIcon.ShowBalloonTip(3500);
            _hasShownTrayTip = true;
        }
    }

    private void ShowWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitApplication()
    {
        SaveWindowState();
        _allowClose = true;
        _refreshCancellation?.Cancel();
        _refreshTimer.Stop();
        _countdownTimer.Stop();
        StopKeepingAwake();
        _quotaService.Dispose();
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
        _trayIconImage?.Dispose();
        Close();
        System.Windows.Application.Current.Shutdown();
    }
}

internal static class NativeIcon
{
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyIcon(IntPtr icon);
}

internal static class NativePower
{
    private static readonly IntPtr BroadcastWindow = new(0xFFFF);
    private static readonly IntPtr MonitorPowerCommand = new(0xF170);
    private static readonly IntPtr PowerOff = new(2);
    private const int SystemCommandMessage = 0x0112;

    [Flags]
    internal enum ExecutionState : uint
    {
        SystemRequired = 0x00000001,
        Continuous = 0x80000000
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern ExecutionState SetThreadExecutionState(ExecutionState executionState);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(
        IntPtr window,
        int message,
        IntPtr parameter,
        IntPtr value);

    internal static void TurnOffDisplays() =>
        SendMessage(BroadcastWindow, SystemCommandMessage, MonitorPowerCommand, PowerOff);
}
