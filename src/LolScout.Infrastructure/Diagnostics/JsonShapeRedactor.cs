using System.Text.Json;

namespace LolScout.Infrastructure.Diagnostics;

public static class JsonShapeRedactor
{
    private static readonly HashSet<string> SafeProtocolFieldNames = new(StringComparer.Ordinal)
    {
        "actions",
        "assists",
        "championId",
        "championName",
        "count",
        "deaths",
        "empty",
        "gameName",
        "games",
        "isMvp",
        "items",
        "kills",
        "matches",
        "myTeam",
        "participants",
        "phase",
        "players",
        "position",
        "puuid",
        "queueType",
        "summonerId",
        "tagLine",
        "teamId",
        "theirTeam",
        "visible",
        "win"
    };

    public static object Describe(JsonElement element) => DescribeElement(element);

    private static object DescribeElement(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => DescribeObject(element),
        JsonValueKind.Array => DescribeArray(element),
        JsonValueKind.String => "string",
        JsonValueKind.Number => "number",
        JsonValueKind.True or JsonValueKind.False => "boolean",
        JsonValueKind.Null => "null",
        _ => "undefined"
    };

    private static object DescribeObject(JsonElement element)
    {
        var result = new Dictionary<string, object?>();
        var anonymousIndex = 0;

        foreach (var property in element.EnumerateObject())
        {
            var isSafeUniqueName = SafeProtocolFieldNames.Contains(property.Name) && !result.ContainsKey(property.Name);
            if (isSafeUniqueName)
            {
                result[property.Name] = DescribeElement(property.Value);
                continue;
            }

            var anonymousName = $"redacted-field-{++anonymousIndex}";
            result[anonymousName] = "redacted-field";
        }

        return result;
    }

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
}
