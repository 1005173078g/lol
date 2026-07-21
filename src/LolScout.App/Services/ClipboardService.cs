using System.Windows;
using System.Runtime.InteropServices;

namespace LolScout.App.Services;

public interface IClipboardService
{
    Task SetTextAsync(string text, CancellationToken cancellationToken = default);
}

public sealed class ClipboardService : IClipboardService
{
    private readonly Action<string> setText;
    private readonly Func<TimeSpan, CancellationToken, Task> delay;

    public ClipboardService() : this(System.Windows.Clipboard.SetText, Task.Delay) { }

    public ClipboardService(Action<string> setText, Func<TimeSpan, CancellationToken, Task> delay)
    {
        this.setText = setText ?? throw new ArgumentNullException(nameof(setText));
        this.delay = delay ?? throw new ArgumentNullException(nameof(delay));
    }

    public async Task SetTextAsync(string text, CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                setText(text);
                return;
            }
            catch (ExternalException) when (attempt < 4)
            {
                await delay(TimeSpan.FromMilliseconds(100), cancellationToken);
            }
        }
        throw new InvalidOperationException("Clipboard retry loop completed unexpectedly.");
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
