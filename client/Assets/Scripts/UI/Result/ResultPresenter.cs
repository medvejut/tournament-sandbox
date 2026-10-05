using TournamentSandbox.Core;
using TournamentSandbox.Net;

namespace TournamentSandbox.UI
{
    public sealed class ResultPresenter
    {
        private const string PracticeNote =
            "Practice score, computed on this device.\nOnline, the server replays your moves and its score is the only one that counts.";

        private readonly MatchResultView _view;

        public ResultPresenter(MatchResultView view)
        {
            _view = view;
        }

        public void ShowPractice(ReplayResult prediction, uint seed) =>
            _view.Show("TIME!", prediction.Score, Summary(prediction), PracticeNote, $"seed {seed}");

        public void ShowSubmitting(MatchView match, ReplayResult prediction) =>
            _view.Show("SUBMITTING...", prediction.Score, Summary(prediction), "Predicted on this device. Sending your moves to the server.", SeedLine(match));

        public void ShowWaiting(MatchView submitted, ReplayResult prediction) =>
            _view.Show("WAITING FOR OPPONENT", submitted.score, Summary(prediction), ScoreNote(submitted, prediction), SeedLine(submitted));

        public void ShowFinal(MatchView final, ReplayResult prediction) =>
            _view.Show(Title(final), final.score, Outcome(final), ScoreNote(final, prediction), SeedLine(final));

        public void ShowSubmitFailed(MatchView match, ReplayResult prediction, string message) =>
            _view.Show("SUBMIT FAILED", prediction.Score, Summary(prediction), message, SeedLine(match));

        public void ShowNote(string note) => _view.ShowNote(note);

        private static string Summary(ReplayResult prediction) =>
            $"{prediction.Daubs} daubs  |  {prediction.Lines} lines  |  {prediction.FalseBingos} false bingos";

        private static string SeedLine(MatchView match) => $"seed {match.Seed}  |  match {match.matchId}";

        private static string Title(MatchView match)
        {
            if (match.status == MatchStatus.Refunded)
                return "REFUNDED";
            switch (match.result)
            {
                case MatchResult.Win:
                    return "YOU WIN!";
                case MatchResult.Loss:
                    return "YOU LOSE";
                default:
                    return "TIE";
            }
        }

        private static string Outcome(MatchView match) =>
            match.status == MatchStatus.Refunded
                ? $"no opponent, fee refunded  |  balance {match.balance}"
                : $"opponent {match.opponentScore}  |  payout {match.payout}{Multiplier(match)}  |  balance {match.balance}";

        private static string Multiplier(MatchView match) => match.multiplier > 1f ? $" (x{match.multiplier:0.#})" : "";

        private static string ScoreNote(MatchView match, ReplayResult prediction)
        {
            if (!string.IsNullOrEmpty(match.rejectedReason))
                return $"The server rejected the move log: {match.rejectedReason}.";
            if (match.score != prediction.Score)
                return $"Server score. It differs from this device's prediction of {prediction.Score}.";
            return "Server score, from its replay of your moves. Matches this device's prediction.";
        }
    }
}
