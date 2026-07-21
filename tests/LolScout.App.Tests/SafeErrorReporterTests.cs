using FluentAssertions;
using LolScout.App.Services;
using Xunit;

namespace LolScout.App.Tests;

public sealed class SafeErrorReporterTests
{
    [Fact]
    public void Watch_failure_emits_only_fixed_safe_diagnostics()
    {
        var lines = new List<string>();
        var reporter = new SafeErrorReporter(lines.Add, () => DateTimeOffset.UnixEpoch);
        var exception = new InvalidOperationException(@"token=real-secret C:\Users\Alice\session.json");

        reporter.ReportWatchFailure(exception);

        lines.Should().ContainSingle();
        lines[0].Should().Contain("APP_WATCH_FAILED").And.Contain("Waiting");
        lines[0].Should().NotContainAny("real-secret", "Alice", "session.json", "InvalidOperationException");
    }
}
