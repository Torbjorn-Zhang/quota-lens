using System.Diagnostics;
using System.Threading;
using System.Windows;
using QuotaLens.Services;

namespace QuotaLens;

public partial class App : System.Windows.Application
{
    private MainWindow? _window;
    private Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var current = Process.GetCurrentProcess();
        var version = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "dev";
        StartupLog.Write(
            $"start v{version} pid={current.Id} session={current.SessionId} " +
            $"args=[{string.Join(" ", e.Args)}] path={Environment.ProcessPath}");
        DispatcherUnhandledException += (_, args) =>
            StartupLog.Write($"unhandled: {args.Exception.GetType().Name}: {args.Exception.Message}");

        var olderInstance = Process.GetProcessesByName(current.ProcessName)
            .FirstOrDefault(process => process.Id != current.Id);
        if (olderInstance is not null)
        {
            StartupLog.Write($"exit: another instance is running (pid={olderInstance.Id})");
            System.Windows.MessageBox.Show(
                "检测到另一个 Quota Lens 正在运行。请先从托盘退出旧版本，再启动当前版本。",
                "Quota Lens",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        _singleInstanceMutex = new Mutex(initiallyOwned: true, "Local\\QuotaLens.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            StartupLog.Write("exit: single-instance mutex already held");
            Shutdown();
            return;
        }

        _window = new MainWindow();
        MainWindow = _window;

        if (e.Args.Contains("--minimized", StringComparer.OrdinalIgnoreCase))
        {
            _window.InitializeInTray();
        }
        else
        {
            _window.Show();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        StartupLog.Write($"exit code={e.ApplicationExitCode}");
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
