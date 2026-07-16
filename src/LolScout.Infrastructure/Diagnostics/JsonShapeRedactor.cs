using System.Text.Json;
using System.Text.RegularExpressions;

namespace LolScout.Infrastructure.Diagnostics;

public static partial class JsonShapeRedactor
{
    public static object Describe(JsonElement element) => DescribeElement(element);

    private static object DescribeElement(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().ToDictionary(
            property => property.Name,
            property => SensitiveFieldName().IsMatch(property.Name)
                ? (object)"redacted-field"
                : DescribeElement(property.Value)),
        JsonValueKind.Array => DescribeArray(element),
        JsonValueKind.String => "string",
        JsonValueKind.Number => "number",
        JsonValueKind.True or JsonValueKind.False => "boolean",
        JsonValueKind.Null => "null",
        _ => "undefined"
    };

    private static object DescribeArray(JsonElement element)
    {
        var length = element.GetArrayLength();
        return new Dictionary<string, object?>
        {
            ["type"] = "array",
            ["length"] = length,
            ["item"] = length == 0 ? null : DescribeElement(element[0])
        };
    }

    [GeneratedRegex("token|cookie|authorization|ticket|session", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveFieldName();
}
