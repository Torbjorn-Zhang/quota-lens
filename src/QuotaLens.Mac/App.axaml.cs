using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using QuotaLens.Services;

namespace QuotaLens.Mac;

public partial class App : Application
{
    private QuotaController? _controller;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (Program.PreviewDirectory is string directory)
            {
                // Rendering needs the dispatcher loop, which starts after this method returns.
                Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        PreviewRenderer.Render(directory);
                    }
                    finally
                    {
                        desktop.Shutdown();
                    }
                });
            }
            else
            {
                _controller = new QuotaController(this, desktop);
                desktop.Exit += (_, args) =>
                {
                    StartupLog.Write($"exit code={args.ApplicationExitCode}");
                    _controller.Dispose();
                };
                _controller.Start();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
