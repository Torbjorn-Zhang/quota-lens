namespace QuotaLens.Services;

/// <summary>
/// Pure geometry for QQ-style edge docking, kept free of WPF types so it can be unit-tested.
/// All values are device-independent pixels in screen space.
/// </summary>
internal static class DockPlacement
{
    /// <summary>A floating panel docks when its frame edge comes this close to a work-area edge.</summary>
    internal const double SnapDistance = 24;

    /// <summary>A docked panel stays docked (and snaps back) unless dragged further than this.</summary>
    internal const double ReleaseDistance = 48;

    /// <summary>
    /// Decides the dock edge after a drag ends. The current edge is sticky up to
    /// <see cref="ReleaseDistance"/>; otherwise the panel snaps to whichever edge it is within
    /// <see cref="SnapDistance"/> of, preferring the right edge, or floats.
    /// </summary>
    internal static DockEdge Resolve(
        DockEdge current,
        double frameLeft,
        double frameRight,
        double workLeft,
        double workRight)
    {
        var gapLeft = frameLeft - workLeft;
        var gapRight = workRight - frameRight;

        if (current == DockEdge.Right && gapRight <= ReleaseDistance) return DockEdge.Right;
        if (current == DockEdge.Left && gapLeft <= ReleaseDistance) return DockEdge.Left;
        if (gapRight <= SnapDistance) return DockEdge.Right;
        if (gapLeft <= SnapDistance) return DockEdge.Left;
        return DockEdge.None;
    }

    /// <summary>
    /// Returns a window top that keeps content of the given height, drawn <paramref name="margin"/>
    /// below the window top, inside the vertical work area. Content taller than the work area is
    /// pinned to the top.
    /// </summary>
    internal static double ClampTop(
        double top,
        double contentHeight,
        double workTop,
        double workBottom,
        double margin)
    {
        var min = workTop - margin;
        var max = Math.Max(min, workBottom - margin - contentHeight);
        return Math.Clamp(top, min, max);
    }
}
