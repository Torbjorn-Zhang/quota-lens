using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using QuotaLens.Services;
using Forms = System.Windows.Forms;

namespace QuotaLens;

/// <summary>
/// QQ-style edge docking. Dragging the widget against the left or right edge of a monitor's work
/// area docks it there as a sidebar: it collapses into a slim strip of ring gauges, slides the full
/// panel out when the cursor rests on the strip, and slides it back once the cursor leaves the
/// panel. Dragging it away from the edge turns it back into the floating widget.
/// </summary>
/// <remarks>
/// The window keeps a fixed width while docked. Everything outside the visible strip or panel is
/// fully transparent, and layered windows pass mouse input through zero-alpha pixels, so the idle
/// strip only blocks the area it actually paints. Hover is tracked by polling the cursor against
/// element bounds rather than by MouseEnter/MouseLeave, which are unreliable across transparent
/// regions and during slide animations.
/// </remarks>
public partial class MainWindow
{
    private const double FloatingWindowWidth = 420;
    private const double DockedWindowWidth = 404;
    private const double ShadowMargin = 16;
    private const double PanelHoverTolerance = 8;
    private static readonly TimeSpan HoverExpandDelay = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan LeaveCollapseDelay = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan OpenGracePeriod = TimeSpan.FromSeconds(5);
    private static readonly Duration ExpandDuration = TimeSpan.FromMilliseconds(170);
    private static readonly Duration CollapseDuration = TimeSpan.FromMilliseconds(130);

    private enum SidebarState
    {
        Floating,
        Collapsed,
        Expanded
    }

    private readonly DispatcherTimer _dockTimer = new() { Interval = TimeSpan.FromMilliseconds(60) };
    private readonly TranslateTransform _panelSlide = new();
    private Forms.ToolStripMenuItem? _dockMenuItem;
    private SidebarState _sidebarState = SidebarState.Floating;
    private double _dockTop;
    private double _stripHeight = 300;
    private DateTime? _hoverStartedAt;
    private DateTime? _outsideSince;
    private DateTime _graceUntil = DateTime.MinValue;
    private int _slideGeneration;
    private bool _expandOnLoad;

    private bool IsDocked => _settings.DockEdge != DockEdge.None;

    /// <summary>Called from the constructor, before the window is shown.</summary>
    private void InitializeDocking()
    {
        GlassFrame.RenderTransform = _panelSlide;
        if (StripFrame.Background is Freezable freezable)
        {
            StripFrame.Background = (System.Windows.Media.Brush)freezable.CloneCurrentValue();
        }

        _dockTimer.Tick += DockTimer_Tick;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        ApplyDockChrome(_settings.DockEdge);
        if (IsDocked)
        {
            // Lay out the strip from the start so the first frame is not the full panel.
            ShowCollapsedVisuals();
        }
    }

    private void ShutdownDocking()
    {
        _dockTimer.Stop();
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
    }

    /// <summary>
    /// Runs on Loaded. The window uses WindowStartupLocation.Manual: with CenterScreen plus
    /// SizeToContent, WPF re-centres the window after Loaded and would undo this placement.
    /// </summary>
    private void RestoreWindowPosition()
    {
        var restored = false;
        if (_settings.WindowLeft is double left && _settings.WindowTop is double top)
        {
            var visible = left + ActualWidth > SystemParameters.VirtualScreenLeft
                          && left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth
                          && top + ActualHeight > SystemParameters.VirtualScreenTop
                          && top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight;
            if (visible)
            {
                Left = left;
                Top = top;
                restored = true;
            }
        }

        if (!restored)
        {
            var area = WorkAreaDip(Forms.Screen.FromPoint(Forms.Control.MousePosition));
            Left = area.Left + (area.Width - ActualWidth) / 2;
            Top = area.Top + (area.Height - ActualHeight) / 2;
        }

        if (IsDocked)
        {
            DockTo(_settings.DockEdge, Top, expanded: false);
        }
    }

    /// <summary>Applies the edge-dependent chrome: width, margins, corners, borders and topmost.</summary>
    private void ApplyDockChrome(DockEdge edge)
    {
        var left = edge == DockEdge.Left;
        var right = edge == DockEdge.Right;

        Width = edge == DockEdge.None ? FloatingWindowWidth : DockedWindowWidth;
        RootGrid.Margin = new Thickness(
            left ? 0 : ShadowMargin,
            ShadowMargin,
            right ? 0 : ShadowMargin,
            ShadowMargin);
        GlassFrame.CornerRadius = new CornerRadius(left ? 0 : 24, right ? 0 : 24, right ? 0 : 24, left ? 0 : 24);
        GlassFrame.BorderThickness = new Thickness(left ? 0 : 1, 1, right ? 0 : 1, 1);

        StripFrame.HorizontalAlignment = left
            ? System.Windows.HorizontalAlignment.Left
            : System.Windows.HorizontalAlignment.Right;
        StripFrame.CornerRadius = left ? new CornerRadius(0, 14, 14, 0) : new CornerRadius(14, 0, 0, 14);
        StripFrame.BorderThickness = left ? new Thickness(0, 1, 1, 1) : new Thickness(1, 1, 0, 1);

        PinButton.Visibility = edge == DockEdge.None ? Visibility.Visible : Visibility.Collapsed;
        DragHintText.Text = edge == DockEdge.None ? "拖到屏幕边缘可贴边" : "拖离边缘取消贴边";
        if (_pinMenuItem is not null) _pinMenuItem.Enabled = edge == DockEdge.None;
        if (_dockMenuItem is not null) _dockMenuItem.Checked = edge != DockEdge.None;
        ApplyTopmost();
    }

    /// <summary>A docked strip must stay above other windows or it would vanish behind them.</summary>
    private void ApplyTopmost()
    {
        Topmost = IsDocked || _settings.AlwaysOnTop;
        UpdatePinVisual();
    }

    private void DockTo(DockEdge edge, double top, bool expanded)
    {
        var changed = _settings.DockEdge != edge;
        _settings.DockEdge = edge;
        ApplyDockChrome(edge);
        _dockTop = top;
        if (expanded)
        {
            ShowExpandedVisuals();
        }
        else
        {
            ShowCollapsedVisuals();
        }

        PlaceDockedWindow();
        _dockTimer.Start();
        if (changed) StartupLog.Write($"dock: {edge}");
    }

    private void Undock()
    {
        if (!IsDocked) return;

        // Keep the glass frame where it is on screen while the margins change underneath it.
        var frameLeft = Left + RootGrid.Margin.Left;
        _dockTimer.Stop();
        _settings.DockEdge = DockEdge.None;
        ApplyDockChrome(DockEdge.None);
        ShowFloatingVisuals();
        Left = frameLeft - RootGrid.Margin.Left;
        StartupLog.Write("dock: None");
    }

    /// <summary>Pins the window to its dock edge and keeps the visible part inside the work area.</summary>
    private void PlaceDockedWindow()
    {
        if (!IsDocked) return;

        var area = WorkAreaDip(DockScreen(_settings.DockEdge));
        Left = _settings.DockEdge == DockEdge.Right ? area.Right - Width : area.Left;
        UpdateLayout();

        if (StripFrame.Visibility == Visibility.Visible && StripFrame.ActualHeight > 0)
        {
            _stripHeight = StripFrame.ActualHeight;
        }

        _dockTop = DockPlacement.ClampTop(_dockTop, _stripHeight, area.Top, area.Bottom, ShadowMargin);
        Top = _sidebarState == SidebarState.Expanded
            ? DockPlacement.ClampTop(_dockTop, GlassFrame.ActualHeight, area.Top, area.Bottom, ShadowMargin)
            : _dockTop;
    }

    private void ShowCollapsedVisuals()
    {
        StopSlide();
        GlassFrame.Visibility = Visibility.Collapsed;
        StripFrame.Visibility = Visibility.Visible;
        _sidebarState = SidebarState.Collapsed;
        _hoverStartedAt = null;
    }

    private void ShowExpandedVisuals()
    {
        StopSlide();
        StripFrame.Visibility = Visibility.Collapsed;
        GlassFrame.Visibility = Visibility.Visible;
        _sidebarState = SidebarState.Expanded;
        _outsideSince = null;
    }

    private void ShowFloatingVisuals()
    {
        StopSlide();
        StripFrame.Visibility = Visibility.Collapsed;
        GlassFrame.Visibility = Visibility.Visible;
        _sidebarState = SidebarState.Floating;
    }

    private void StopSlide()
    {
        _slideGeneration++;
        _panelSlide.BeginAnimation(TranslateTransform.XProperty, null);
        _panelSlide.X = 0;
    }

    /// <param name="grace">
    /// Keeps the panel open for a few seconds even if the cursor is elsewhere, for opens that do not
    /// come from hovering (tray menu, docking toggle). The grace ends as soon as the cursor enters.
    /// </param>
    private void Expand(bool animate, bool grace)
    {
        if (!IsDocked) return;
        _graceUntil = grace ? DateTime.UtcNow + OpenGracePeriod : DateTime.MinValue;
        _outsideSince = null;
        if (_sidebarState == SidebarState.Expanded) return;

        var wasHidden = GlassFrame.Visibility != Visibility.Visible;
        StripFrame.Visibility = Visibility.Collapsed;
        GlassFrame.Visibility = Visibility.Visible;
        _sidebarState = SidebarState.Expanded;
        PlaceDockedWindow();

        _slideGeneration++;
        if (!animate)
        {
            _panelSlide.BeginAnimation(TranslateTransform.XProperty, null);
            _panelSlide.X = 0;
            return;
        }

        var slide = new DoubleAnimation
        {
            To = 0,
            Duration = ExpandDuration,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        if (wasHidden) slide.From = HiddenOffset();
        _panelSlide.BeginAnimation(TranslateTransform.XProperty, slide);
    }

    private void Collapse(bool animate)
    {
        if (_sidebarState != SidebarState.Expanded) return;
        _sidebarState = SidebarState.Collapsed;
        _hoverStartedAt = null;

        if (!animate)
        {
            FinishCollapse();
            return;
        }

        var generation = ++_slideGeneration;
        var slide = new DoubleAnimation
        {
            To = HiddenOffset(),
            Duration = CollapseDuration,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        slide.Completed += (_, _) =>
        {
            // A re-expand during the slide bumps the generation and must win.
            if (generation == _slideGeneration && _sidebarState == SidebarState.Collapsed)
            {
                FinishCollapse();
            }
        };
        _panelSlide.BeginAnimation(TranslateTransform.XProperty, slide);
    }

    private void FinishCollapse()
    {
        ShowCollapsedVisuals();
        PlaceDockedWindow();
    }

    /// <summary>X offset that pushes the panel just past the docked edge, out of the window.</summary>
    private double HiddenOffset()
    {
        var width = GlassFrame.ActualWidth > 0 ? GlassFrame.ActualWidth : DockedWindowWidth - ShadowMargin;
        return _settings.DockEdge == DockEdge.Left ? -(width + 2) : width + 2;
    }

    private void DockTimer_Tick(object? sender, EventArgs e)
    {
        if (!IsDocked || !IsVisible || _sidebarState == SidebarState.Floating) return;

        var cursor = Forms.Control.MousePosition;
        var buttonsDown = Forms.Control.MouseButtons != Forms.MouseButtons.None;
        var now = DateTime.UtcNow;

        if (_sidebarState == SidebarState.Collapsed)
        {
            // The panel is still visible while it slides shut; coming back onto it reopens it.
            if (!buttonsDown && GlassFrame.IsVisible && IsCursorOver(GlassFrame, cursor, 0))
            {
                Expand(animate: true, grace: false);
                return;
            }

            if (!buttonsDown && IsCursorOver(StripFrame, cursor, 0))
            {
                _hoverStartedAt ??= now;
                if (now - _hoverStartedAt.Value >= HoverExpandDelay) Expand(animate: true, grace: false);
            }
            else
            {
                _hoverStartedAt = null;
            }
            return;
        }

        if (IsCursorOver(GlassFrame, cursor, PanelHoverTolerance))
        {
            _graceUntil = DateTime.MinValue;
            _outsideSince = null;
            return;
        }

        // Never pull the panel away mid-drag or mid-click, and honour an explicit open.
        if (buttonsDown || now < _graceUntil)
        {
            _outsideSince = null;
            return;
        }

        _outsideSince ??= now;
        if (now - _outsideSince.Value >= LeaveCollapseDelay) Collapse(animate: true);
    }

    private static bool IsCursorOver(FrameworkElement element, System.Drawing.Point cursor, double tolerance)
    {
        if (!element.IsVisible || PresentationSource.FromVisual(element) is null) return false;
        try
        {
            var point = element.PointFromScreen(new System.Windows.Point(cursor.X, cursor.Y));
            return point.X >= -tolerance
                   && point.Y >= -tolerance
                   && point.X <= element.ActualWidth + tolerance
                   && point.Y <= element.ActualHeight + tolerance;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>The monitor that currently holds most of the window.</summary>
    private Forms.Screen CurrentScreen()
    {
        var handle = new WindowInteropHelper(this).Handle;
        return handle != IntPtr.Zero
            ? Forms.Screen.FromHandle(handle)
            : Forms.Screen.FromPoint(new System.Drawing.Point((int)Left, (int)Top));
    }

    /// <summary>
    /// Monitor whose edge the docked strip should hug. Starting from the monitor under the window,
    /// it steps outward past any neighbour, so the strip never sits on an edge shared with another
    /// monitor where the cursor crosses between screens all the time.
    /// </summary>
    private Forms.Screen DockScreen(DockEdge edge)
    {
        var screen = CurrentScreen();
        for (var hops = 0; hops < 16; hops++)
        {
            var neighbour = Neighbour(screen, edge);
            if (neighbour is null) break;
            screen = neighbour;
        }
        return screen;
    }

    /// <summary>The monitor touching <paramref name="screen"/> on the given side, if any.</summary>
    private static Forms.Screen? Neighbour(Forms.Screen screen, DockEdge edge)
    {
        var bounds = screen.Bounds;
        foreach (var other in Forms.Screen.AllScreens)
        {
            if (other.DeviceName == screen.DeviceName) continue;
            var candidate = other.Bounds;
            var overlapsVertically = candidate.Top < bounds.Bottom && candidate.Bottom > bounds.Top;
            if (!overlapsVertically) continue;
            if (edge == DockEdge.Right && Math.Abs(candidate.Left - bounds.Right) <= 2) return other;
            if (edge == DockEdge.Left && Math.Abs(candidate.Right - bounds.Left) <= 2) return other;
        }
        return null;
    }

    /// <summary>Work area of a monitor in device-independent pixels.</summary>
    private Rect WorkAreaDip(Forms.Screen screen)
    {
        var area = screen.WorkingArea;
        var fromDevice = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice
                         ?? System.Windows.Media.Matrix.Identity;
        return new Rect(
            fromDevice.Transform(new System.Windows.Point(area.Left, area.Top)),
            fromDevice.Transform(new System.Windows.Point(area.Right, area.Bottom)));
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (IsDocked && IsLoaded) PlaceDockedWindow();
        }));

    /// <summary>Runs after any drag of the panel header or the strip has been released.</summary>
    private void OnDragCompleted()
    {
        var screen = CurrentScreen();
        var area = WorkAreaDip(screen);
        // An edge shared with another monitor never snaps; see DockScreen.
        var workLeft = Neighbour(screen, DockEdge.Left) is null ? area.Left : double.NegativeInfinity;
        var workRight = Neighbour(screen, DockEdge.Right) is null ? area.Right : double.PositiveInfinity;
        var frameLeft = Left + RootGrid.Margin.Left;
        var frameRight = Left + Width - RootGrid.Margin.Right;
        var edge = DockPlacement.Resolve(_settings.DockEdge, frameLeft, frameRight, workLeft, workRight);

        if (edge == DockEdge.None)
        {
            Undock();
        }
        else
        {
            DockTo(edge, Top, expanded: _sidebarState != SidebarState.Collapsed);
        }
        SaveWindowState();
    }

    private void StripFrame_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        try
        {
            DragMove();
            OnDragCompleted();
        }
        catch (InvalidOperationException)
        {
            // The mouse button may be released before DragMove starts.
        }
    }

    /// <summary>Tray toggle for users who do not discover drag-to-edge.</summary>
    private void ToggleDocking()
    {
        if (!IsVisible) Show();

        if (IsDocked)
        {
            // Step clear of the snap distance so the next drag does not immediately re-dock.
            var edge = _settings.DockEdge;
            Undock();
            Left += edge == DockEdge.Right ? -40 : 40;
        }
        else
        {
            var area = WorkAreaDip(CurrentScreen());
            var center = Left + Width / 2;
            var edge = center >= area.Left + area.Width / 2 ? DockEdge.Right : DockEdge.Left;
            DockTo(edge, Top, expanded: true);
            _graceUntil = DateTime.UtcNow + OpenGracePeriod;
        }

        SaveWindowState();
        Activate();
    }

    private void RenderStrip(QuotaSnapshot snapshot)
    {
        var now = DateTimeOffset.Now;
        RenderStripProvider(snapshot.Codex, StripCodexBadge, StripCodexRings, StripCodexRows, now);
        RenderStripProvider(snapshot.Claude, StripClaudeBadge, StripClaudeRings, StripClaudeRows, now);
    }

    /// <summary>
    /// Concentric rings, one per quota window from the outside in (5-hour, 7-day, model allowance,
    /// at most three), each drawn in its identity colour with the arc showing what remains. Below
    /// them one row per window, in the same order and colour, gives the remaining percentage and the
    /// reset time (clock time within 24 hours, otherwise the date).
    /// </summary>
    private static void RenderStripProvider(
        ProviderQuota quota,
        FrameworkElement badge,
        Canvas rings,
        System.Windows.Controls.Panel rows,
        DateTimeOffset now)
    {
        rings.Children.Clear();
        rows.Children.Clear();
        var windows = quota.IsAvailable ? quota.Windows.Take(StripRingCount).ToList() : new List<QuotaWindow>();

        for (var index = 0; index < StripRingCount; index++)
        {
            var window = index < windows.Count ? windows[index] : null;
            if (window is null && index > 0) break;
            AddRing(rings, index, window);
        }

        if (!quota.IsAvailable)
        {
            badge.Opacity = 0.45;
            rows.Children.Add(new TextBlock
            {
                Text = "未连接",
                FontSize = 9.5,
                Foreground = Brush("#FF6B7A"),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center
            });
            return;
        }

        badge.Opacity = 1;
        foreach (var window in windows)
        {
            rows.Children.Add(CreateStripRow(window, now));
        }
    }

    private const int StripRingCount = 3;
    private const double StripRingSize = 48;
    private const double StripRingStroke = 4;
    private const double StripRingGap = 2;

    /// <summary>
    /// Draws ring <paramref name="index"/> (0 = outermost): a faint full track plus a clockwise arc
    /// from twelve o'clock covering the remaining share. A null window draws the track only.
    /// </summary>
    private static void AddRing(Canvas canvas, int index, QuotaWindow? window)
    {
        var center = StripRingSize / 2;
        var radius = center - StripRingStroke / 2 - index * (StripRingStroke + StripRingGap);
        if (radius <= StripRingStroke) return;

        var track = new System.Windows.Shapes.Ellipse
        {
            Width = radius * 2,
            Height = radius * 2,
            Stroke = Brush("#1FFFFFFF"),
            StrokeThickness = StripRingStroke
        };
        Canvas.SetLeft(track, center - radius);
        Canvas.SetTop(track, center - radius);
        canvas.Children.Add(track);

        if (window is null) return;
        var fraction = Math.Clamp(window.RemainingPercent, 0, 100) / 100;
        if (fraction <= 0.005) return;

        var stroke = BarBrush(window);
        if (fraction >= 0.999)
        {
            var full = new System.Windows.Shapes.Ellipse
            {
                Width = radius * 2,
                Height = radius * 2,
                Stroke = stroke,
                StrokeThickness = StripRingStroke
            };
            Canvas.SetLeft(full, center - radius);
            Canvas.SetTop(full, center - radius);
            canvas.Children.Add(full);
            return;
        }

        var angle = fraction * 2 * Math.PI;
        var start = new System.Windows.Point(center, center - radius);
        var end = new System.Windows.Point(center + radius * Math.Sin(angle), center - radius * Math.Cos(angle));
        var figure = new PathFigure { StartPoint = start, IsClosed = false, IsFilled = false };
        figure.Segments.Add(new ArcSegment(
            end,
            new System.Windows.Size(radius, radius),
            0,
            isLargeArc: fraction > 0.5,
            SweepDirection.Clockwise,
            isStroked: true));
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);

        canvas.Children.Add(new System.Windows.Shapes.Path
        {
            Data = geometry,
            Stroke = stroke,
            StrokeThickness = StripRingStroke,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        });
    }

    private static FrameworkElement CreateStripRow(QuotaWindow window, DateTimeOffset now)
    {
        var row = new Grid { Width = 52, Margin = new Thickness(0, 1, 0, 0) };
        row.Children.Add(new TextBlock
        {
            Text = $"{window.RemainingPercent:0}%",
            FontSize = 9.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = LevelBrush(window.RemainingPercent, IdentityBrush(window)),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left
        });
        row.Children.Add(new TextBlock
        {
            Text = QuotaWindowLegend.CompactReset(window.ResetsAt, now),
            FontSize = 9.5,
            Foreground = Brush("#B8C6DCF0"),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right
        });
        return row;
    }
}
