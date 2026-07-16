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
        composition = AppComposition.Create();
        viewModel = new(composition.Coordinator, new ClipboardService(), new WpfUiDispatcher(Dispatcher));
        var window = new MainWindow(viewModel);
        MainWindow = window;
        tray = new TrayIconService(
            () => ShowMainWindow(window),
            () => viewModel.RefreshCommand.Execute(null),
            ExitApplicationAsync);
        shutdownCoordinator = new ShutdownCoordinator(
            () => viewModel.DisposeAsync(),
            [tray, composition]);
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
        (MainWindow as MainWindow)?.CloseForExit();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        shutdownCoordinator?.StopAsync().GetAwaiter().GetResult();
        base.OnExit(e);
    }
}
