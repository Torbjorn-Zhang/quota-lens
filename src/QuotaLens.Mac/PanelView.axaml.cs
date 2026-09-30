using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using QuotaLens.Mac.Rendering;
using QuotaLens.Services;

namespace QuotaLens.Mac;

/// <summary>
/// The detailed quota view: per provider, concentric rings with the service letter in the centre
/// and one row per quota window with the remaining percentage and a live reset countdown.
/// </summary>
public partial class PanelView : UserControl
{
    private readonly List<(TextBlock Text, QuotaWindow Window)> _resetTexts = new();

    private bool _launchAtLogin;
    private bool _alerts;

    public event EventHandler? RefreshRequested;
    public event EventHandler? CloseRequested;
    public event EventHandler? LaunchAtLoginToggled;
    public event EventHandler? AlertsToggled;
    public event EventHandler? ScreenOffRequested;
    public event EventHandler? QuitRequested;

    public PanelView()
    {
        InitializeComponent();
        WireGadget(RefreshButton, () => RefreshRequested?.Invoke(this, EventArgs.Empty));
        WireGadget(CloseButton, () => CloseRequested?.Invoke(this, EventArgs.Empty));
        WireGadget(ScreenOffButton, () => ScreenOffRequested?.Invoke(this, EventArgs.Empty));
        WireGadget(QuitButton, () => QuitRequested?.Invoke(this, EventArgs.Empty));
        WireToggle(LaunchAtLoginToggle, () => _launchAtLogin, () => LaunchAtLoginToggled?.Invoke(this, EventArgs.Empty));
        WireToggle(AlertsToggle, () => _alerts, () => AlertsToggled?.Invoke(this, EventArgs.Empty));
        SetOptions(launchAtLogin: false, alerts: false);
    }

    /// <summary>Shows the current settings on the two toggle pills.</summary>
    public void SetOptions(bool launchAtLogin, bool alerts)
    {
        _launchAtLogin = launchAtLogin;
        _alerts = alerts;
        StyleToggle(LaunchAtLoginToggle, LaunchAtLoginText, "登录时启动", launchAtLogin);
        StyleToggle(AlertsToggle, AlertsText, "低额度提醒", alerts);
    }

    private static void StyleToggle(Border pill, TextBlock text, string label, bool on)
    {
        pill.Background = QuotaPalette.Hex(on ? "#2638D6A3" : "#14FFFFFF");
        pill.BorderBrush = QuotaPalette.Hex(on ? "#5538D6A3" : "#20FFFFFF");
        text.Text = on ? "✓ " + label : label;
        text.Foreground = QuotaPalette.Hex(on ? "#9FF0D2" : "#CDE0ED");
    }

    private static void WireToggle(Border pill, Func<bool> isOn, Action onClick)
    {
        pill.PointerEntered += (_, _) => pill.Background = QuotaPalette.Hex(isOn() ? "#3638D6A3" : "#2EFFFFFF");
        pill.PointerExited += (_, _) => pill.Background = QuotaPalette.Hex(isOn() ? "#2638D6A3" : "#14FFFFFF");
        pill.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton == MouseButton.Left) onClick();
        };
    }

    public void SetRefreshing(bool refreshing)
    {
        RefreshButton.IsEnabled = !refreshing;
        RefreshButton.Opacity = refreshing ? 0.45 : 1;
    }

    public void Render(QuotaSnapshot snapshot)
    {
        UpdatedText.Text = $"更新 {snapshot.FetchedAt:HH:mm:ss}";
        _resetTexts.Clear();
        ProvidersHost.Children.Clear();
        ProvidersHost.Children.Add(CreateProviderCard("C", snapshot.Codex));
        ProvidersHost.Children.Add(CreateProviderCard("A", snapshot.Claude));
        UpdateCountdowns();
    }

    /// <summary>The reset lines currently shown, for the self-test log.</summary>
    public IEnumerable<string> CountdownTexts() => _resetTexts.Select(entry => entry.Text.Text ?? string.Empty);

    public void UpdateCountdowns()
    {
        var now = DateTimeOffset.Now;
        foreach (var (text, window) in _resetTexts)
        {
            text.Text = QuotaWindowLegend.FormatResetCountdown(window.ResetsAt, now);
        }
    }

    /// <summary>Plain borders act as buttons, so no theme template is needed to draw them.</summary>
    private static void WireGadget(Border gadget, Action onClick)
    {
        var normal = gadget.Background;
        gadget.PointerEntered += (_, _) => gadget.Background = QuotaPalette.Hex("#2EFFFFFF");
        gadget.PointerExited += (_, _) => gadget.Background = normal;
        gadget.PointerReleased += (_, e) =>
        {
            if (gadget.IsEnabled && e.InitialPressMouseButton == MouseButton.Left) onClick();
        };
    }

    private Control CreateProviderCard(string letter, ProviderQuota quota)
    {
        var windows = quota.IsAvailable ? quota.Windows.Take(RingPainter.MaxRings).ToList() : new List<QuotaWindow>();

        var rings = new Grid { Width = 68, Height = 68, VerticalAlignment = VerticalAlignment.Top };
        rings.Children.Add(new RingGauge { Windows = windows, Stroke = 5.5, Gap = 2.5 });
        rings.Children.Add(new TextBlock
        {
            Text = letter,
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            Foreground = quota.IsAvailable ? QuotaPalette.Text : QuotaPalette.Muted,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });

        var details = new StackPanel { Spacing = 5, Margin = new Thickness(14, 0, 0, 0) };
        details.Children.Add(new TextBlock
        {
            Text = quota.IsAvailable ? $"{quota.Provider} · {quota.Plan}" : quota.Provider,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 0, 0, 2)
        });

        if (!quota.IsAvailable)
        {
            details.Children.Add(new TextBlock
            {
                Text = quota.Error,
                FontSize = 11,
                Foreground = QuotaPalette.Hex("#FF8E9A"),
                TextWrapping = TextWrapping.Wrap
            });
        }

        foreach (var window in quota.Windows)
        {
            details.Children.Add(CreateWindowRow(window));
        }

        if (quota.IsAvailable && !string.IsNullOrWhiteSpace(quota.ExtraInfo))
        {
            details.Children.Add(new TextBlock
            {
                Text = quota.ExtraInfo,
                FontSize = 10.5,
                Foreground = QuotaPalette.Muted,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0)
            });
        }

        var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        layout.Children.Add(rings);
        Grid.SetColumn(details, 1);
        layout.Children.Add(details);

        return new Border
        {
            Background = QuotaPalette.Hex("#16FFFFFF"),
            BorderBrush = QuotaPalette.Hex("#24FFFFFF"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(12, 12, 14, 12),
            Child = layout
        };
    }

    private Control CreateWindowRow(QuotaWindow window)
    {
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        header.Children.Add(new TextBlock
        {
            Text = window.Name,
            FontSize = 11.5,
            Foreground = QuotaPalette.Identity(window),
            VerticalAlignment = VerticalAlignment.Bottom
        });
        var value = new TextBlock
        {
            Text = $"{window.RemainingPercent:0}%",
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            Foreground = QuotaPalette.Level(window.RemainingPercent, QuotaPalette.Text)
        };
        Grid.SetColumn(value, 1);
        header.Children.Add(value);

        var reset = new TextBlock { FontSize = 10.5, Foreground = QuotaPalette.Muted };
        _resetTexts.Add((reset, window));

        var row = new StackPanel();
        row.Children.Add(header);
        row.Children.Add(reset);
        return row;
    }
}
