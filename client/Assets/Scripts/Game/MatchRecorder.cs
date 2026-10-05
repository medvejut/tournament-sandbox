using System;
using System.Collections.Generic;
using TournamentSandbox.Core;

namespace TournamentSandbox.Game
{
    // Refuses taps that would get the whole log rejected; a false bingo only costs points.
    public sealed class MatchRecorder
    {
        private readonly List<Move> _moves = new List<Move>(Rules.MaxMoves);
        private readonly bool[] _daubed = new bool[Rules.CellCount];
        private readonly bool[] _claimed = new bool[Bingo.Lines.Count];

        public BingoGame Game { get; }
        public CallScheduler Calls { get; }
        public IReadOnlyList<Move> Moves => _moves;
        public ReplayResult Prediction { get; private set; }
        public int LastMoveT => _moves.Count == 0 ? -1 : _moves[_moves.Count - 1].t;

        public event Action<MatchRecorder> Changed;

        public MatchRecorder(BingoGame game)
        {
            Game = game ?? throw new ArgumentNullException(nameof(game));
            Calls = new CallScheduler(game);
            _daubed[Rules.FreeCell] = true;
            Prediction = Bingo.Replay(game, _moves);
        }

        public MatchRecorder(BingoGame game, IEnumerable<Move> savedMoves) : this(game)
        {
            foreach (var move in savedMoves)
            {
                var result = move.kind == Move.Daub ? TryDaub(move.cell, move.t) : TryClaimBingo(move.t, out _);
                if (result != TapResult.Accepted)
                    throw new ArgumentException($"saved move {move} is not replayable: {result}");
            }
        }

        public bool IsDaubed(int cell) => _daubed[cell];

        public bool IsLineClaimed(int lineIndex) => _claimed[lineIndex];

        public TapResult TryDaub(int cell, int t)
        {
            if (cell == Rules.FreeCell)
                return TapResult.FreeCell;
            if (cell < 0 || cell >= Rules.CellCount)
                throw new ArgumentOutOfRangeException(nameof(cell));
            if (_daubed[cell])
                return TapResult.AlreadyDaubed;

            var callTime = Calls.CallTimeOf(Game.Card[cell]);
            if (callTime < 0 || t < callTime)
                return TapResult.NotCalledYet;
            if (t - callTime < Rules.MinReactionMs)
                return TapResult.TooSoonAfterCall;

            var timing = CheckTiming(t);
            if (timing != TapResult.Accepted)
                return timing;

            _daubed[cell] = true;
            Append(Move.DaubAt(cell, t));
            return TapResult.Accepted;
        }

        // newLines is 0 for a false bingo, which is still recorded.
        public TapResult TryClaimBingo(int t, out int newLines)
        {
            newLines = 0;
            var timing = CheckTiming(t);
            if (timing != TapResult.Accepted)
                return timing;

            newLines = Bingo.ClaimCompletedLines(_daubed, _claimed);
            Append(Move.BingoAt(t));
            return TapResult.Accepted;
        }

        private TapResult CheckTiming(int t)
        {
            if (t < 0 || t > Rules.MatchDurationMs)
                return TapResult.OutOfTime;
            if (_moves.Count >= Rules.MaxMoves)
                return TapResult.MoveLimitReached;
            if (_moves.Count > 0 && t - LastMoveT < Rules.MinMoveIntervalMs)
                return TapResult.TooFastAfterLastMove;
            return TapResult.Accepted;
        }

        private void Append(Move move)
        {
            _moves.Add(move);
            Prediction = Bingo.Replay(Game, _moves);
            Changed?.Invoke(this);
        }
    }
}
