using System.Text.Json;
using FluentAssertions;
using LolScout.Infrastructure.Diagnostics;
using Xunit;

namespace LolScout.Infrastructure.Tests;

public sealed class RedactingLoggerTests
{
    [Fact]
    public void Log_writes_only_approved_diagnostic_fields()
    {
        using var output = new StringWriter();
        var logger = new RedactingLogger(output, () => new DateTimeOffset(2026, 7, 16, 8, 30, 0, TimeSpan.Zero));

        logger.Log(new Dictionary<string, object?>
        {
            ["Phase"] = "History",
            ["ErrorCode"] = "TIMEOUT",
            ["StatusCode"] = 504,
            ["MissingField"] = "games",
            ["DurationMs"] = 1250,
            ["Authorization"] = "Basic secret",
            ["Cookie"] = "session=secret",
            ["token"] = "secret",
            ["PlayerId"] = "real-player-id",
            ["Response"] = "{ complete = response }",
            ["Path"] = @"C:\Users\real-user\AppData"
        });

        using var document = JsonDocument.Parse(output.ToString());
        var root = document.RootElement;
        root.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(
            "Timestamp", "Phase", "ErrorCode", "StatusCode", "MissingField", "DurationMs");
        root.GetProperty("Timestamp").GetString().Should().Be("2026-07-16T08:30:00.0000000+00:00");
        output.ToString().Should().NotContainAny("secret", "real-player-id", "complete", @"C:\Users");
    }

    [Fact]
    public void Log_ignores_caller_supplied_timestamp()
    {
        using var output = new StringWriter();
        var logger = new RedactingLogger(output, () => DateTimeOffset.UnixEpoch);

        logger.Log(new Dictionary<string, object?> { ["Timestamp"] = "forged", ["Phase"] = "Waiting" });

        using var document = JsonDocument.Parse(output.ToString());
        document.RootElement.GetProperty("Timestamp").GetString().Should().Be("1970-01-01T00:00:00.0000000+00:00");
    }
}
