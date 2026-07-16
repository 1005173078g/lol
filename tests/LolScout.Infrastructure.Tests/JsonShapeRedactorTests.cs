using System.Text.Json;
using FluentAssertions;
using LolScout.Infrastructure.Diagnostics;
using Xunit;

namespace LolScout.Infrastructure.Tests;

public sealed class JsonShapeRedactorTests
{
    [Fact]
    public void Describe_never_emits_values_or_secret_headers()
    {
        using var doc = JsonDocument.Parse("""{"token":"secret123","players":[{"name":"real-player"}]}""");

        var text = JsonSerializer.Serialize(JsonShapeRedactor.Describe(doc.RootElement));

        text.Should().Contain("players").And.NotContain("secret123").And.NotContain("real-player");
        text.Should().Contain("redacted-field");
    }

    [Fact]
    public void Describe_reports_types_array_length_and_only_first_item_shape()
    {
        using var doc = JsonDocument.Parse("""{"items":[{"visible":true},{"secondOnly":"hidden"}],"count":2,"empty":null}""");

        var text = JsonSerializer.Serialize(JsonShapeRedactor.Describe(doc.RootElement));

        text.Should().Contain("\"length\":2").And.Contain("visible").And.Contain("boolean")
            .And.Contain("number").And.Contain("null").And.NotContain("secondOnly").And.NotContain("hidden");
    }

    [Theory]
    [InlineData("cookie")]
    [InlineData("Authorization")]
    [InlineData("matchTicket")]
    [InlineData("sessionId")]
    public void Describe_redacts_sensitive_field_names_case_insensitively(string fieldName)
    {
        using var doc = JsonDocument.Parse($"{{\"{fieldName}\":{{\"nested\":\"value\"}}}}");

        var description = JsonSerializer.Serialize(JsonShapeRedactor.Describe(doc.RootElement));

        description.Should().Contain(fieldName).And.Contain("redacted-field").And.NotContain("nested");
    }
}
