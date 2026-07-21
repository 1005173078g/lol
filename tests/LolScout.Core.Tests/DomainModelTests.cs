using FluentAssertions;
using LolScout.Core.Domain;
using Xunit;

namespace LolScout.Core.Tests;

public sealed class DomainModelTests
{
    [Fact]
    public void PlayerIdentity_rejects_blank_game_name()
    {
        var act = () => new PlayerIdentity(" ", "CN1", "联盟一区");

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null, "TAG", "CN")]
    [InlineData("Game", null, "CN")]
    [InlineData("Game", " ", "CN")]
    [InlineData("Game", "TAG", null)]
    [InlineData("Game", "TAG", " ")]
    public void PlayerIdentity_rejects_null_or_blank_components(string? gameName, string? tagLine, string? region)
    {
        var act = () => new PlayerIdentity(gameName!, tagLine!, region!);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void PlayerIdentity_trims_all_components()
    {
        new PlayerIdentity(" Game ", " TAG ", " CN ").Should().Be(new PlayerIdentity("Game", "TAG", "CN"));
    }
}
