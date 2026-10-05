using System.Collections.Generic;
using TournamentSandbox.Core;
using TournamentSandbox.Net;

namespace TournamentSandbox.Game
{
    public sealed class ResumedMatch
    {
        public MatchView Match { get; }
        public IReadOnlyList<Move> Moves { get; }
        public long ServerElapsedMs { get; } // since the server started the match, not the countdown

        public ResumedMatch(MatchView match, IReadOnlyList<Move> moves, long serverElapsedMs)
        {
            Match = match;
            Moves = moves;
            ServerElapsedMs = serverElapsedMs;
        }

        public bool IsPlaying => Match.status == MatchStatus.Playing;
    }
}
