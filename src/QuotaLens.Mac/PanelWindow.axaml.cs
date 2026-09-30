using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace QuotaLens.Mac;

/// <summary>
/// Borderless, transparent window hosting <see cref="PanelView"/>, opened from the menu bar menu and
/// anchored under the right end of the menu bar. Clicking anywhere else hides it again.
/// </summary>
public partial class PanelWindow : Window
{
    private readonly DispatcherTimer _countdownTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    public event EventHandler? RefreshRequested;

    public PanelWindow()
    {
        InitializeComponent();
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        View.RefreshRequested += (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty);
        View.CloseRequested += (_, _) => Hide();
        Deactivated += (_, _) => Hide();
        _countdownTimer.Tick += (_, _) => View.UpdateCountdowns();
    }

    public void Render(QuotaSnapshot snapshot) => View.Render(snapshot);

    public void SetRefreshing(bool refreshing) => View.SetRefreshing(refreshing);

    /// <summary>Shows the panel under the right end of the menu bar of the primary screen.</summary>
    public void ShowUnderMenuBar()
    {
        var screen = Screens.Primary ?? Screens.All.FirstOrDefault();
        if (screen is not null)
        {
            var area = screen.WorkingArea;
            var scale = screen.Scaling;
            var width = (int)Math.Ceiling(View.Width * scale);
            Position = new PixelPoint(area.Right - width, area.Y);
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
        _countdownTimer.Stop();
        base.Hide();
    }
}
