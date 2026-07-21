using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LolScout.Core.Domain;

namespace LolScout.Infrastructure.Diagnostics;

/// <summary>Writes one JSON object per line after validating every value as a safe scalar.</summary>
public sealed partial class RedactingLogger
{
    private const double MaximumDurationMs = 604_800_000d;
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
        var buffer = new ArrayBufferWriter<byte>();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("Timestamp", utcNow().ToString("O"));
            WriteValidatedString(json, properties, "Phase", ValidatePhase);
            WriteValidatedString(json, properties, "ErrorCode", ValidateErrorCode);
            if (properties.TryGetValue("StatusCode", out var status) && status is int code && code is >= 100 and <= 599)
                json.WriteNumber("StatusCode", code);
            WriteValidatedString(json, properties, "MissingField", ValidateMissingField);
            if (properties.TryGetValue("DurationMs", out var duration) && TryGetDuration(duration, out var milliseconds))
                json.WriteNumber("DurationMs", milliseconds);
            json.WriteEndObject();
        }
        output.WriteLine(Encoding.UTF8.GetString(buffer.WrittenSpan));
    }

    private static void WriteValidatedString(Utf8JsonWriter json, IReadOnlyDictionary<string, object?> properties,
        string name, Func<object?, string?> validator)
    {
        if (!properties.TryGetValue(name, out var value)) return;
        json.WriteString(name, validator(value) ?? "redacted");
    }

    private static string? ValidatePhase(object? value)
    {
        if (value is GamePhase phase) return phase.ToString();
        if (value is not string text) return null;
        return Enum.TryParse<GamePhase>(text, ignoreCase: false, out var parsed) && parsed.ToString() == text ? text : null;
    }

    private static string? ValidateErrorCode(object? value) =>
        value is string text && ErrorCodePattern().IsMatch(text) ? text : null;

    private static string? ValidateMissingField(object? value) =>
        value is string text && MissingFieldPattern().IsMatch(text) ? text : null;

    private static bool TryGetDuration(object? value, out double milliseconds)
    {
        milliseconds = value switch
        {
            byte number => number,
            ushort number => number,
            uint number => number,
            ulong number when number <= MaximumDurationMs => number,
            sbyte number => number,
            short number => number,
            int number => number,
            long number => number,
            float number => number,
            double number => number,
            decimal number when number <= (decimal)MaximumDurationMs => (double)number,
            _ => double.NaN
        };
        return double.IsFinite(milliseconds) && milliseconds >= 0 && milliseconds <= MaximumDurationMs;
    }

    [GeneratedRegex("^[A-Z0-9_.-]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex ErrorCodePattern();

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9]*(?:\\.[A-Za-z][A-Za-z0-9]*)*$", RegexOptions.CultureInvariant)]
    private static partial Regex MissingFieldPattern();
}
