using Avalonia;
using QuotaLens.Services;

namespace QuotaLens.Mac;

internal static class Program
{
    /// <summary>Set by <c>--render-preview &lt;dir&gt;</c>: render the icon and panel with sample data, then exit.</summary>
    internal static string? PreviewDirectory { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        var previewIndex = Array.IndexOf(args, "--render-preview");
        if (previewIndex >= 0 && previewIndex + 1 < args.Length)
        {
            PreviewDirectory = args[previewIndex + 1];
        }
        else
        {
            if (!SingleInstance.TryAcquire())
            {
                StartupLog.Write("exit: another instance is running");
                return 0;
            }

            StartupLog.Write(
                $"start v{AppPaths.Version} {AppPaths.PlatformName} pid={Environment.ProcessId} " +
                $"args=[{string.Join(" ", args)}] path={Environment.ProcessPath}");
        }

        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
        }
        catch (Exception ex)
        {
            StartupLog.Write($"fatal: {ex.GetType().Name}: {ex.Message}");
            throw;
        }
    }

    /// <summary>A menu bar utility: no Dock icon and no application menu.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new MacOSPlatformOptions { ShowInDock = false })
            .LogToTrace();
}

/// <summary>
/// Opening the app twice from Finder is handled by macOS, but launching the binary directly (for
/// example from the login agent while an instance runs) is not, so an exclusive lock file guards it.
/// </summary>
internal static class SingleInstance
{
    private static FileStream? _lock;

    internal static bool TryAcquire()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            _lock = new FileStream(
                Path.Combine(AppPaths.DataDirectory, "instance.lock"),
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }
}
