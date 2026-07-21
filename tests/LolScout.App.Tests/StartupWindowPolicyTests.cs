using FluentAssertions;
using Xunit;

namespace LolScout.App.Tests;

public sealed class StartupWindowPolicyTests
{
    [Fact]
    public void Independent_window_is_shown_on_startup() =>
        StartupWindowPolicy.ShouldShowOnStartup().Should().BeTrue();
}
