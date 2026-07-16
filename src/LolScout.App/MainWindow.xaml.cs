using System.ComponentModel;
using System.Windows;
using LolScout.App.ViewModels;

namespace LolScout.App;

public partial class MainWindow : Window
{
    private bool allowClose;

    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.ShouldShowWindow)
            && DataContext is MainWindowViewModel viewModel
            && viewModel.ConsumeShowWindowRequest())
        {
            Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!allowClose)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }

    internal void CloseForExit()
    {
        allowClose = true;
        Close();
    }
}
