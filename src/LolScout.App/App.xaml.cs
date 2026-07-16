using System.Windows;
using LolScout.App.Services;
using LolScout.App.ViewModels;

namespace LolScout.App;

public partial class App : System.Windows.Application
{
    private MainWindowViewModel? viewModel;
    private TrayIconService? tray;
    private AppComposition? composition;

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
            ExitApplication);
        viewModel.Start();
        window.Show();
    }

    private static void ShowMainWindow(Window window)
    {
        window.Show();
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
    }

    private void ExitApplication()
    {
        tray?.Dispose();
        tray = null;
        (this.MainWindow as LolScout.App.MainWindow)?.CloseForExit();
        Shutdown();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        tray?.Dispose();
        if (viewModel is not null) await viewModel.DisposeAsync();
        composition?.Dispose();
        base.OnExit(e);
    }
}
