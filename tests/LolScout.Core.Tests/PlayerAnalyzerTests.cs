using FluentAssertions;
using LolScout.Core.Analysis;
using LolScout.Core.Domain;
using Xunit;

namespace LolScout.Core.Tests;

public sealed class PlayerAnalyzerTests
{
    [Fact]
    public void Analyze_uses_at_most_twenty_ranked_matches_and_explicit_mvp_flags()
    {
        var matches = Enumerable.Range(0, 25)
            .Select(i => new RecentMatch(i < 12, i < 4, i < 14 ? "中路" : "辅助", 103, 6, 3, 9))
            .ToArray();

        var result = PlayerAnalyzer.Analyze(matches, 103);

        result.MatchCount.Should().Be(20);
        result.WinRate.Should().Be(0.60);
        result.MvpRate.Should().Be(0.20);
        result.PrimaryPosition.Should().Be("中路");
        result.PrimaryPositionRate.Should().Be(0.70);
    }

    [Fact]
    public void Analyze_returns_null_mvp_rate_when_any_flag_is_missing()
    {
        var matches = new[]
        {
            new RecentMatch(true, true, "上路", 122, 1, 1, 1),
            new RecentMatch(false, null, "上路", 122, 1, 1, 1)
        };

        var result = PlayerAnalyzer.Analyze(matches, 122);

        result.MvpRate.Should().BeNull();
    }

    [Fact]
    public void Analyze_calculates_selected_champion_stats_from_matching_games_only()
    {
        var matches = new[]
        {
            new RecentMatch(true, false, "打野", 64, 6, 0, 4),
            new RecentMatch(false, false, "中路", 103, 1, 2, 3),
            new RecentMatch(false, false, "打野", 64, 2, 4, 2)
        };

        var result = PlayerAnalyzer.Analyze(matches, 64);

        result.ChampionMatchCount.Should().Be(2);
        result.ChampionWinRate.Should().Be(0.50);
        result.ChampionKda.Should().Be(5.50);
    }

    [Fact]
    public void Analyze_hides_position_and_champion_rates_when_samples_are_missing()
    {
        var result = PlayerAnalyzer.Analyze(Array.Empty<RecentMatch>(), 103);

        result.MatchCount.Should().Be(0);
        result.WinRate.Should().Be(0);
        result.MvpRate.Should().BeNull();
        result.PrimaryPosition.Should().BeNull();
        result.PrimaryPositionRate.Should().BeNull();
        result.ChampionMatchCount.Should().Be(0);
        result.ChampionWinRate.Should().BeNull();
        result.ChampionKda.Should().BeNull();
    }
}
