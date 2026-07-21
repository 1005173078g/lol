using LolScout.Core.Domain;

namespace LolScout.Core.Analysis;

public static class BroadcastFormatter
{
    private const string Disclaimer = "仅供参考";

    public static string Format(
        IReadOnlyList<(LiveParticipant Participant, PlayerAnalysis Analysis)> players,
        int maxLength)
    {
        ArgumentNullException.ThrowIfNull(players);

        if (maxLength < Disclaimer.Length)
        {
            return string.Empty;
        }

        var cards = players.Select(PlayerCard.Create).ToList();
        var fieldsByRemovalOrder = new Action<PlayerCard>[]
        {
            card => card.ShowPosition = false,
            card => card.ShowHighKda = false,
            card => card.ShowOverallWinRate = false,
            card => card.ShowChampionWinRate = false
        };

        foreach (var removeField in fieldsByRemovalOrder)
        {
            if (Render(cards).Length <= maxLength)
            {
                break;
            }

            cards.ForEach(removeField);
        }

        while (cards.Count > 0 && Render(cards).Length > maxLength)
        {
            cards.RemoveAt(cards.Count - 1);
        }

        var text = Render(cards);
        if (text.Length <= maxLength)
        {
            return text;
        }

        return string.Empty;
    }

    private static string Render(IReadOnlyList<PlayerCard> cards)
    {
        if (cards.Count == 0)
        {
            return Disclaimer;
        }

        return $"{string.Join("；", cards.Select(card => card.Render()))} | {Disclaimer}";
    }

    private static string Percentage(double value) =>
        $"{Math.Round(value * 100, MidpointRounding.AwayFromZero):0}%";

    private sealed class PlayerCard
    {
        private PlayerCard(LiveParticipant participant, PlayerAnalysis analysis)
        {
            Participant = participant;
            Analysis = analysis;
            ShowChampionWinRate = analysis.ChampionMatchCount >= 3 && analysis.ChampionWinRate is not null;
            ShowOverallWinRate = analysis.MatchCount > 0;
            ShowHighKda = true;
            ShowPosition = !string.IsNullOrWhiteSpace(analysis.PrimaryPosition);
        }

        public LiveParticipant Participant { get; }

        public PlayerAnalysis Analysis { get; }

        public bool ShowChampionWinRate { get; set; }

        public bool ShowOverallWinRate { get; set; }

        public bool ShowHighKda { get; set; }

        public bool ShowPosition { get; set; }

        public static PlayerCard Create((LiveParticipant Participant, PlayerAnalysis Analysis) value) =>
            new(value.Participant, value.Analysis);

        public string Render()
        {
            var fields = new List<string> { $"{Participant.Player.GameName}({Participant.ChampionName})" };

            if (ShowChampionWinRate)
            {
                fields.Add($"英雄胜率{Percentage(Analysis.ChampionWinRate!.Value)}({Analysis.ChampionMatchCount}场)");
            }

            if (ShowOverallWinRate)
            {
                fields.Add($"总胜率{Percentage(Analysis.WinRate)}({Analysis.MatchCount}场)");
            }

            if (ShowHighKda)
            {
                fields.Add($"高KDA{Analysis.HighKdaMatchCount}场");
            }

            if (ShowPosition)
            {
                var rate = Analysis.PrimaryPositionRate is null
                    ? string.Empty
                    : Percentage(Analysis.PrimaryPositionRate.Value);
                fields.Add($"常用{Analysis.PrimaryPosition}{rate}");
            }

            return string.Join(' ', fields);
        }
    }
}
