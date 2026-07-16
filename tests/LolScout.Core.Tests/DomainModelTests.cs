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
}
