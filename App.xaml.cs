using System.Diagnostics;
using System.Threading;
using System.Windows;

namespace QuotaLens;

public partial class App : System.Windows.Application
{
    private MainWindow? _window;
    private Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var current = Process.GetCurrentProcess();
        var olderInstance = Process.GetProcessesByName(current.ProcessName)
            .FirstOrDefault(process => process.Id != current.Id);
        if (olderInstance is not null)
        {
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
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
