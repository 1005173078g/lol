using CommunityToolkit.Mvvm.ComponentModel;
using LolScout.Core.Domain;

namespace LolScout.App.ViewModels;

public sealed partial class PlayerCardViewModel : ObservableObject
{
    [ObservableProperty] private string playerName = "";
    [ObservableProperty] private string championName = "";
    [ObservableProperty] private string status = "查询中";
    [ObservableProperty] private string overall = "等待数据";
    [ObservableProperty] private string mvp = "等待数据";
    [ObservableProperty] private string position = "等待数据";
    [ObservableProperty] private string championPerformance = "等待数据";

    internal void Update(PlayerScoutState state)
    {
        PlayerName = $"{state.Participant.Player.GameName}#{state.Participant.Player.TagLine}";
        ChampionName = state.Participant.ChampionName;
        if (state.Error is not null)
        {
            Status = "查询失败";
            Overall = Mvp = Position = ChampionPerformance = "暂不可用";
            return;
        }
        if (state.Analysis is null)
        {
            Status = "查询中";
            Overall = Mvp = Position = ChampionPerformance = "等待数据";
            return;
        }
        var analysis = state.Analysis;
        Status = "已完成";
        Overall = $"近 {analysis.MatchCount} 场胜率 {Percent(analysis.WinRate)}";
        Mvp = analysis.MvpRate is null ? "MVP 不可用" : $"MVP {Percent(analysis.MvpRate.Value)}";
        Position = analysis.PrimaryPosition is null ? "常用位置不可用" : $"常用 {analysis.PrimaryPosition} {Percent(analysis.PrimaryPositionRate ?? 0)}";
        ChampionPerformance = analysis.ChampionMatchCount == 0
            ? "本局英雄近期无样本"
            : $"本局英雄 {analysis.ChampionMatchCount} 场 / 胜率 {Percent(analysis.ChampionWinRate ?? 0)} / KDA {analysis.ChampionKda:0.0}";
    }

    private static string Percent(double value) => $"{Math.Round(value * 100):0}%";
}
