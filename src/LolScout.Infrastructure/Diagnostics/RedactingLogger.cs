using System.Text.Json;

namespace LolScout.Infrastructure.Diagnostics;

/// <summary>Writes one JSON object per line and drops every property not explicitly approved.</summary>
public sealed class RedactingLogger
{
    private static readonly string[] AllowedProperties =
        ["Phase", "ErrorCode", "StatusCode", "MissingField", "DurationMs"];

    private readonly TextWriter output;
    private readonly Func<DateTimeOffset> utcNow;

    public RedactingLogger(TextWriter output, Func<DateTimeOffset>? utcNow = null)
    {
        ArgumentNullException.ThrowIfNull(output);
        this.output = output;
        this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public void Log(IReadOnlyDictionary<string, object?> properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        var safe = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["Timestamp"] = utcNow().ToString("O")
        };
        foreach (var name in AllowedProperties)
            if (properties.TryGetValue(name, out var value))
                safe[name] = value;

        output.WriteLine(JsonSerializer.Serialize(safe));
    }
}
