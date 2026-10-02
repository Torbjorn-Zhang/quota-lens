using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace QuotaLens.Mac;

/// <summary>
/// Borderless, transparent window hosting <see cref="PanelView"/>, dropped from the menu bar icon.
/// Clicking anywhere else hides it again.
/// </summary>
public partial class PanelWindow : Window
{
    private readonly DispatcherTimer _countdownTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    public PanelWindow()
    {
        InitializeComponent();
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        View.CloseRequested += (_, _) => Hide();
        Deactivated += (_, _) => Hide();
        _countdownTimer.Tick += (_, _) => View.UpdateCountdowns();
    }

    public PanelView Panel => View;

    /// <summary>When the panel was last hidden; a click on the icon right after it closed the panel must not reopen it.</summary>
    public DateTime HiddenAtUtc { get; private set; } = DateTime.MinValue;

    /// <summary>
    /// Shows the panel just below the menu bar of the primary screen, horizontally centred on
    /// <paramref name="anchorCenterX"/> (in Avalonia position units) when given, else at the right
    /// edge, and always kept inside the work area.
    /// </summary>
    public void ShowBelowMenuBar(double? anchorCenterX, double unitsPerDip)
    {
        var screen = Screens.Primary ?? Screens.All.FirstOrDefault();
        if (screen is not null)
        {
            var area = screen.WorkingArea;
            var width = (int)Math.Ceiling(View.Width * unitsPerDip);
            var x = anchorCenterX is double center ? (int)Math.Round(center - width / 2.0) : area.Right - width;
            x = Math.Clamp(x, area.X, Math.Max(area.X, area.Right - width));
            Position = new PixelPoint(x, area.Y);
        }

        Show();
        Activate();
        _countdownTimer.Start();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // Keep the window for reuse; closing it from the system just hides it.
        e.Cancel = true;
        Hide();
    }

    public override void Hide()
    {
        if (IsVisible) HiddenAtUtc = DateTime.UtcNow;
        _countdownTimer.Stop();
        base.Hide();
    }
}
