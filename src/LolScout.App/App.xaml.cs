using System.Windows;
using LolScout.App.Services;
using LolScout.App.ViewModels;

namespace LolScout.App;

public partial class App : System.Windows.Application
{
    private MainWindowViewModel? viewModel;
    private TrayIconService? tray;
    private AppComposition? composition;
    private ShutdownCoordinator? shutdownCoordinator;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var errors = new SafeErrorReporter(line => System.Diagnostics.Trace.TraceError(line));
        composition = AppComposition.Create();
        viewModel = new(composition.Coordinator, new ClipboardService(), new WpfUiDispatcher(Dispatcher),
            reportWatchFailure: errors.ReportWatchFailure);
        var window = new MainWindow(viewModel);
        MainWindow = window;
        tray = new TrayIconService(
            () => ShowMainWindow(window),
            () => viewModel.RefreshCommand.Execute(null),
            ExitApplicationAsync,
            viewModel.ReportLifecycleErrorAsync);
        shutdownCoordinator = new ShutdownCoordinator(
            () => viewModel.DisposeAsync(),
            viewModel.StopFallback,
            [tray, composition],
            () => Dispatcher.InvokeAsync(() =>
            {
                window.CloseForExit();
                Shutdown();
            }).Task,
            async error =>
            {
                errors.ReportLifecycleFailure(error);
                if (!Dispatcher.HasShutdownStarted) await viewModel.ReportLifecycleErrorAsync(error);
            });
        viewModel.Start();
    }

    private static void ShowMainWindow(Window window)
    {
        window.Show();
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
    }

    private async Task ExitApplicationAsync()
    {
        if (shutdownCoordinator is not null) await shutdownCoordinator.StopAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        shutdownCoordinator?.StopFallback();
        base.OnExit(e);
    }
}
