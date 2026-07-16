using System.Text.Json;
using FluentAssertions;
using LolScout.Infrastructure.Diagnostics;
using LolScout.Core.Domain;
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

    [Fact]
    public void Log_never_serializes_or_stringifies_complex_values()
    {
        using var output = new StringWriter();
        var malicious = new ThrowingValue();
        var logger = new RedactingLogger(output, () => DateTimeOffset.UnixEpoch);

        logger.Log(new Dictionary<string, object?>
        {
            ["Phase"] = malicious,
            ["ErrorCode"] = new InvalidOperationException("token=exception-secret"),
            ["StatusCode"] = new { Cookie = "nested-secret" },
            ["MissingField"] = new[] { "Authorization", "array-secret" },
            ["DurationMs"] = malicious
        });

        malicious.ToStringCalls.Should().Be(0);
        output.ToString().Should().NotContainAny("exception-secret", "nested-secret", "array-secret", "Authorization");
        using var document = JsonDocument.Parse(output.ToString());
        document.RootElement.GetProperty("Phase").GetString().Should().Be("redacted");
        document.RootElement.GetProperty("ErrorCode").GetString().Should().Be("redacted");
        document.RootElement.GetProperty("MissingField").GetString().Should().Be("redacted");
        document.RootElement.TryGetProperty("StatusCode", out _).Should().BeFalse();
        document.RootElement.TryGetProperty("DurationMs", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("../../player-id")]
    [InlineData(@"C:\Users\player")]
    [InlineData("player#CN1")]
    [InlineData("field with spaces")]
    public void Log_redacts_unsafe_missing_field_names(string value)
    {
        using var output = new StringWriter();
        var logger = new RedactingLogger(output, () => DateTimeOffset.UnixEpoch);

        logger.Log(new Dictionary<string, object?> { ["MissingField"] = value });

        output.ToString().Should().NotContain(value);
        using var document = JsonDocument.Parse(output.ToString());
        document.RootElement.GetProperty("MissingField").GetString().Should().Be("redacted");
    }

    [Fact]
    public void Log_accepts_only_bounded_safe_primitive_values()
    {
        using var output = new StringWriter();
        var logger = new RedactingLogger(output, () => DateTimeOffset.UnixEpoch);

        logger.Log(new Dictionary<string, object?>
        {
            ["Phase"] = GamePhase.InGame,
            ["ErrorCode"] = "LCU.TIMEOUT-1",
            ["StatusCode"] = 504,
            ["MissingField"] = "games.items",
            ["DurationMs"] = 123.5d
        });

        using var document = JsonDocument.Parse(output.ToString());
        document.RootElement.GetProperty("Phase").GetString().Should().Be("InGame");
        document.RootElement.GetProperty("DurationMs").GetDouble().Should().Be(123.5d);
    }

    private sealed class ThrowingValue
    {
        public int ToStringCalls { get; private set; }
        public string Token => "property-secret";
        public override string ToString() { ToStringCalls++; throw new InvalidOperationException("must not stringify"); }
    }
}
