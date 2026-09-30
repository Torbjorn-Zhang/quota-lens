using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using QuotaLens.Mac.Rendering;

namespace QuotaLens.Mac;

/// <summary>
/// <c>--render-preview &lt;dir&gt;</c>: renders the menu bar icon (dark and light) and the panel with
/// sample data to PNG files, so the design can be checked on any machine and in CI without an
/// account or a visible window.
/// </summary>
internal static class PreviewRenderer
{
    internal static void Render(string directory)
    {
        Directory.CreateDirectory(directory);
        var snapshot = SampleSnapshot();

        File.WriteAllBytes(Path.Combine(directory, "menubar-icon-dark.png"), TrayIconRenderer.RenderPng(snapshot, darkMenuBar: true));
        File.WriteAllBytes(Path.Combine(directory, "menubar-icon-light.png"), TrayIconRenderer.RenderPng(snapshot, darkMenuBar: false));

        File.WriteAllBytes(Path.Combine(directory, "app-icon.png"), AppIconRenderer.RenderPng());

        // A detached UserControl has no template to present its content, so render its frame
        // directly; the frame still inherits the view's font and colour settings. Rendered at 1x:
        // RenderTargetBitmap.Render applies a non-96 DPI twice for visuals.
        var view = new PanelView();
        view.Render(snapshot);
        var frame = view.Frame;
        frame.Measure(new Size(view.Width, double.PositiveInfinity));
        var size = new Size(view.Width, frame.DesiredSize.Height);
        frame.Arrange(new Rect(size));

        using var bitmap = new RenderTargetBitmap(
            new PixelSize((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height)),
            new Vector(96, 96));
        bitmap.Render(frame);
        bitmap.Save(Path.Combine(directory, "panel.png"));
    }

    /// <summary>Realistic numbers, including one nearly exhausted window to show the warning colours.</summary>
    internal static QuotaSnapshot SampleSnapshot()
    {
        var now = DateTimeOffset.Now;
        var codex = new ProviderQuota("Codex", "plus", new[]
        {
            new QuotaWindow("5 小时", 6, now.AddHours(2.3)),
            new QuotaWindow("7 天", 1, now.AddDays(6.1))
        }, "附加 credits：0");
        var claude = new ProviderQuota("Claude Code", "Max 20×", new[]
        {
            new QuotaWindow("5 小时", 15, now.AddHours(3.1)),
            new QuotaWindow("7 天", 64, now.AddDays(2.5)),
            new QuotaWindow("Fable 周额度", 86, now.AddDays(2.5), IsModelScoped: true)
        });
        return new QuotaSnapshot(codex, claude, now);
    }
}
