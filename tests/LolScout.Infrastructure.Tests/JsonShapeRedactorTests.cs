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
    [InlineData("real-player")]
    [InlineData("123456789012345678")]
    [InlineData("secret123.token-material")]
    [InlineData("Authorization")]
    public void Describe_anonymizes_unknown_dynamic_and_sensitive_field_names(string fieldName)
    {
        using var doc = JsonDocument.Parse($"{{\"{fieldName}\":{{\"nested\":\"value\"}}}}");

        var description = JsonSerializer.Serialize(JsonShapeRedactor.Describe(doc.RootElement));

        description.Should().Contain("redacted-field-1").And.NotContain(fieldName).And.NotContain("value");
    }

    [Fact]
    public void Describe_anonymizes_dynamic_keys_in_nested_objects_and_arrays()
    {
        using var doc = JsonDocument.Parse("""{"players":[{"real-player":{"123456789012345678":"token-material"}}]}""");

        var description = JsonSerializer.Serialize(JsonShapeRedactor.Describe(doc.RootElement));

        description.Should().Contain("players").And.Contain("redacted-field-1")
            .And.NotContain("real-player").And.NotContain("123456789012345678").And.NotContain("token-material");
    }

    [Fact]
    public void Describe_handles_duplicate_properties_deterministically()
    {
        using var doc = JsonDocument.Parse("""{"players":[],"players":[{"name":"hidden"}],"dynamic":1,"dynamic":2}""");

        var first = JsonSerializer.Serialize(JsonShapeRedactor.Describe(doc.RootElement));
        var second = JsonSerializer.Serialize(JsonShapeRedactor.Describe(doc.RootElement));

        first.Should().Be(second).And.Contain("players").And.Contain("redacted-field-1")
            .And.Contain("redacted-field-2").And.Contain("redacted-field-3").And.NotContain("dynamic").And.NotContain("hidden");
    }
}
