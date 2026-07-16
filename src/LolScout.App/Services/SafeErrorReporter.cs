using System.IO;
using LolScout.Core.Domain;
using LolScout.Infrastructure.Diagnostics;

namespace LolScout.App.Services;

public sealed class SafeErrorReporter
{
    private readonly Action<string> sink;
    private readonly Func<DateTimeOffset> utcNow;

    public SafeErrorReporter(Action<string> sink, Func<DateTimeOffset>? utcNow = null)
    {
        this.sink = sink ?? throw new ArgumentNullException(nameof(sink));
        this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public void ReportWatchFailure(Exception exception) => Report(exception, "APP_WATCH_FAILED");
    public void ReportLifecycleFailure(Exception exception) => Report(exception, "APP_LIFECYCLE_FAILED");

    private void Report(Exception exception, string errorCode)
    {
        ArgumentNullException.ThrowIfNull(exception);
        using var output = new StringWriter();
        new RedactingLogger(output, utcNow).Log(new Dictionary<string, object?>
        {
            ["Phase"] = GamePhase.Waiting,
            ["ErrorCode"] = errorCode
        });
        sink(output.ToString().TrimEnd('\r', '\n'));
    }
}
