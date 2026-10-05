// Port of server/src/game.ts; spec/vectors.json pins both sides.

using System;
using System.Collections.Generic;

namespace TournamentSandbox.Core
{
    public static class Bingo
    {
        public static readonly IReadOnlyList<int[]> Lines = BuildLines(Rules.Size);

        public static BingoGame GenerateGame(uint seed)
        {
            var random = new XorShift32(seed);
            var card = new int[Rules.CellCount];

            var columnNumbers = new List<int>(Rules.NumbersPerColumn);
            for (var column = 0; column < Rules.Size; column++)
            {
                columnNumbers.Clear();
                for (var i = 0; i < Rules.NumbersPerColumn; i++)
                {
                    columnNumbers.Add(column * Rules.NumbersPerColumn + i + 1);
                }
                Shuffle.InPlace(columnNumbers, random);
                for (var row = 0; row < Rules.Size; row++)
                {
                    card[row * Rules.Size + column] = columnNumbers[row];
                }
            }
            card[Rules.FreeCell] = 0;

            var allNumbers = new List<int>(Rules.Size * Rules.NumbersPerColumn);
            for (var number = 1; number <= Rules.Size * Rules.NumbersPerColumn; number++)
            {
                allNumbers.Add(number);
            }
            Shuffle.InPlace(allNumbers, random);
            return new BingoGame(card, allNumbers.GetRange(0, Rules.CallCount).ToArray());
        }

        public static ReplayResult Replay(uint seed, IReadOnlyList<Move> moves) => Replay(GenerateGame(seed), moves);

        public static ReplayResult Replay(BingoGame game, IReadOnlyList<Move> moves)
        {
            if (moves.Count > Rules.MaxMoves)
                return ReplayResult.Reject(RejectReason.TooManyMoves, Rules.MaxMoves);

            var card = game.Card;
            var callTimeByNumber = new Dictionary<int, int>(game.Calls.Length);
            for (var i = 0; i < game.Calls.Length; i++)
            {
                callTimeByNumber[game.Calls[i]] = Rules.CallTimeMs(i);
            }

            var daubed = new bool[card.Length];
            daubed[Rules.FreeCell] = true;
            var claimed = new bool[Lines.Count];

            var score = 0;
            var daubs = 0;
            var lines = 0;
            var falseBingos = 0;
            var lastT = 0;

            for (var i = 0; i < moves.Count; i++)
            {
                var move = moves[i];
                if (move.t < 0 || move.t > Rules.MatchDurationMs)
                    return ReplayResult.Reject(RejectReason.OutsideMatchTime, i);
                if (i > 0 && move.t - lastT < Rules.MinMoveIntervalMs)
                    return ReplayResult.Reject(RejectReason.ImplausibleSpeed, i);
                lastT = move.t;

                if (move.kind == Move.Daub)
                {
                    var cell = move.cell;
                    if (cell < 0 || cell >= card.Length || cell == Rules.FreeCell)
                        return ReplayResult.Reject(RejectReason.CellOutOfRange, i);
                    if (daubed[cell])
                        return ReplayResult.Reject(RejectReason.CellAlreadyDaubed, i);
                    if (!callTimeByNumber.TryGetValue(card[cell], out var callT))
                        return ReplayResult.Reject(RejectReason.NumberNeverCalled, i);
                    if (move.t < callT)
                        return ReplayResult.Reject(RejectReason.DaubBeforeCall, i);
                    if (move.t - callT < Rules.MinReactionMs)
                        return ReplayResult.Reject(RejectReason.ImplausibleReaction, i);

                    daubed[cell] = true;
                    daubs++;
                    score += Rules.DaubPoints + Rules.SpeedBonus(move.t - callT);
                }
                else if (move.kind == Move.Bingo)
                {
                    var newLines = ClaimCompletedLines(daubed, claimed);
                    if (newLines == 0)
                    {
                        falseBingos++;
                        score -= Rules.FalseBingoPenalty;
                    }
                    else
                    {
                        lines += newLines;
                        score += newLines * Rules.LinePoints;
                    }
                }
                else
                {
                    return ReplayResult.Reject(RejectReason.MalformedMove, i);
                }
            }

            return ReplayResult.Success(Math.Max(0, score), daubs, lines, falseBingos);
        }

        public static int ClaimCompletedLines(bool[] daubed, bool[] claimed)
        {
            var newLines = 0;
            for (var lineIndex = 0; lineIndex < Lines.Count; lineIndex++)
            {
                if (claimed[lineIndex] || !IsComplete(Lines[lineIndex], daubed))
                    continue;
                claimed[lineIndex] = true;
                newLines++;
            }
            return newLines;
        }

        public static bool IsComplete(int[] line, bool[] daubed)
        {
            foreach (var cell in line)
            {
                if (!daubed[cell])
                    return false;
            }
            return true;
        }

        private static int[][] BuildLines(int size)
        {
            var lines = new List<int[]>();
            for (var row = 0; row < size; row++)
            {
                var line = new int[size];
                for (var column = 0; column < size; column++)
                {
                    line[column] = row * size + column;
                }
                lines.Add(line);
            }
            for (var column = 0; column < size; column++)
            {
                var line = new int[size];
                for (var row = 0; row < size; row++)
                {
                    line[row] = row * size + column;
                }
                lines.Add(line);
            }
            var diagonal = new int[size];
            var antiDiagonal = new int[size];
            for (var i = 0; i < size; i++)
            {
                diagonal[i] = i * size + i;
                antiDiagonal[i] = i * size + (size - 1 - i);
            }
            lines.Add(diagonal);
            lines.Add(antiDiagonal);
            return lines.ToArray();
        }
    }
}
