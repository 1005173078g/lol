using System.Windows;

namespace LolScout.App.Services;

public interface IClipboardService
{
    Task SetTextAsync(string text, CancellationToken cancellationToken = default);
}

public sealed class ClipboardService : IClipboardService
{
    public Task SetTextAsync(string text, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        System.Windows.Clipboard.SetText(text);
        return Task.CompletedTask;
    }
}

public interface IUiDispatcher
{
    Task InvokeAsync(Action action);
}

public sealed class WpfUiDispatcher(System.Windows.Threading.Dispatcher dispatcher) : IUiDispatcher
{
    public Task InvokeAsync(Action action) => dispatcher.InvokeAsync(action).Task;
}
