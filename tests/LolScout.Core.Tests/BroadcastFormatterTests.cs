using FluentAssertions;
using LolScout.Core.Analysis;
using LolScout.Core.Domain;
using Xunit;

namespace LolScout.Core.Tests;

public sealed class BroadcastFormatterTests
{
    [Fact]
    public void Format_never_exceeds_chat_limit()
    {
        var text = BroadcastFormatter.Format(FiveAnalyses(), 180);

        text.Length.Should().BeLessThanOrEqualTo(180);
        text.Should().Contain("仅供参考");
    }

    [Fact]
    public void Format_includes_all_available_fields_when_they_fit()
    {
        var text = BroadcastFormatter.Format([AnalysisFor("甲")], 180);

        text.Should().Contain("英雄胜率67%(3场)");
        text.Should().Contain("总胜率55%(20场)");
        text.Should().Contain("MVP20%");
        text.Should().Contain("常用中路70%");
    }

    [Fact]
    public void Format_omits_champion_win_rate_when_fewer_than_three_games_were_played()
    {
        var item = AnalysisFor("甲");
        var analysis = item.Analysis with { ChampionMatchCount = 2 };

        var text = BroadcastFormatter.Format([(item.Participant, analysis)], 180);

        text.Should().NotContain("英雄胜率");
        text.Should().Contain("总胜率");
    }

    [Fact]
    public void Format_removes_lower_priority_fields_first_when_constrained()
    {
        var text = BroadcastFormatter.Format([AnalysisFor("甲")], 40);

        text.Should().Contain("英雄胜率67%(3场)");
        text.Should().Contain("总胜率55%(20场)");
        text.Should().NotContain("MVP");
        text.Should().NotContain("常用");
        text.Length.Should().BeLessThanOrEqualTo(40);
    }

    private static (LiveParticipant Participant, PlayerAnalysis Analysis)[] FiveAnalyses() =>
        Enumerable.Range(0, 5).Select(i => AnalysisFor($"玩家{i + 1}")).ToArray();

    private static (LiveParticipant Participant, PlayerAnalysis Analysis) AnalysisFor(string name) =>
        (
            new LiveParticipant(new PlayerIdentity(name, "CN1", "联盟一区"), 200, 103, "阿狸"),
            new PlayerAnalysis(20, 0.55, 0.20, "中路", 0.70, 3, 2d / 3d, 4.5)
        );
}
