using LolScout.Core.Domain;

namespace LolScout.Core.Analysis;

public static class PlayerAnalyzer
{
    private const int MaximumMatchCount = 20;

    public static PlayerAnalysis Analyze(IReadOnlyList<RecentMatch> matches, int championId)
    {
        ArgumentNullException.ThrowIfNull(matches);

        var sample = matches.Take(MaximumMatchCount).ToArray();
        var matchCount = sample.Length;
        var primaryPosition = sample
            .Where(match => !string.IsNullOrWhiteSpace(match.Position))
            .GroupBy(match => match.Position)
            .OrderByDescending(group => group.Count())
            .FirstOrDefault();
        var championMatches = sample.Where(match => match.ChampionId == championId).ToArray();

        return new PlayerAnalysis(
            matchCount,
            matchCount == 0 ? 0 : (double)sample.Count(match => match.Won) / matchCount,
            matchCount == 0 || sample.Any(match => match.IsMvp is null)
                ? null
                : (double)sample.Count(match => match.IsMvp is true) / matchCount,
            primaryPosition?.Key,
            primaryPosition is null ? null : (double)primaryPosition.Count() / matchCount,
            championMatches.Length,
            championMatches.Length == 0
                ? null
                : (double)championMatches.Count(match => match.Won) / championMatches.Length,
            championMatches.Length == 0
                ? null
                : championMatches.Average(match =>
                    (double)(match.Kills + match.Assists) / Math.Max(1, match.Deaths)));
    }
}
