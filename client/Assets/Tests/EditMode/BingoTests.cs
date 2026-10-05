using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TournamentSandbox.Core;

namespace TournamentSandbox.Tests
{
    // Mirrors server/test/game.test.ts.
    public sealed class BingoTests
    {
        private const uint Seed = 20261004;
        private BingoGame _game;

        [SetUp]
        public void SetUp()
        {
            _game = Bingo.GenerateGame(Seed);
        }

        [TestCase(1u)]
        [TestCase(2u)]
        [TestCase(Seed)]
        [TestCase(uint.MaxValue)]
        public void GenerateGame_AnySeed_ProducesAValidCardAnd60UniqueCalls(uint seed)
        {
            var game = Bingo.GenerateGame(seed);
            Assert.That(game.Card.Length, Is.EqualTo(Rules.CellCount));
            Assert.That(game.Card[Rules.FreeCell], Is.EqualTo(0));
            for (var cell = 0; cell < Rules.CellCount; cell++)
            {
                if (cell == Rules.FreeCell)
                    continue;
                var column = cell % Rules.Size;
                Assert.That(game.Card[cell], Is.InRange(column * 15 + 1, column * 15 + 15), $"cell {cell}");
            }
            Assert.That(game.Card.Distinct().Count(), Is.EqualTo(Rules.CellCount));
            Assert.That(game.Calls.Length, Is.EqualTo(60));
            Assert.That(game.Calls.Distinct().Count(), Is.EqualTo(60));
            Assert.That(game.Calls.All(number => number >= 1 && number <= 75));
        }

        [Test]
        public void XorShift32_ZeroSeed_IsRemapped()
        {
            Assert.That(new XorShift32(0).NextUInt(), Is.EqualTo(new XorShift32(0x9E3779B9).NextUInt()));
            Assert.That(new XorShift32(0).NextUInt(), Is.Not.EqualTo(0u));
        }

        [Test]
        public void XorShift32Next_BadBound_Throws()
        {
            var random = new XorShift32(1);
            Assert.Throws<ArgumentOutOfRangeException>(() => random.Next(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => random.Next(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => random.Next((1UL << 32) + 1));
            Assert.DoesNotThrow(() => random.Next(1UL << 32));
        }

        [Test]
        public void Lines_AreRowsColumnsThenDiagonals()
        {
            Assert.That(Bingo.Lines.Count, Is.EqualTo(12));
            Assert.That(Bingo.Lines[0], Is.EqualTo(new[] { 0, 1, 2, 3, 4 }));
            Assert.That(Bingo.Lines[5], Is.EqualTo(new[] { 0, 5, 10, 15, 20 }));
            Assert.That(Bingo.Lines[10], Is.EqualTo(new[] { 0, 6, 12, 18, 24 }));
            Assert.That(Bingo.Lines[11], Is.EqualTo(new[] { 4, 8, 12, 16, 20 }));
        }

        [Test]
        public void Replay_EmptyLog_ScoresZero()
        {
            var result = Bingo.Replay(Seed, Array.Empty<Move>());
            Assert.That(result.Ok && result.Score == 0 && result.Daubs == 0, result.ToString());
        }

        [TestCase(200, 148)]
        [TestCase(2500, 125)]
        [TestCase(5000, 100)]
        [TestCase(7000, 100)]
        public void Replay_DaubAfterReaction_AddsSpeedBonus(int reactionMs, int expectedScore)
        {
            var (cell, callTime) = OnCardCall();
            var result = Bingo.Replay(Seed, new[] { Move.DaubAt(cell, callTime + reactionMs) });
            Assert.That(result.Ok, result.ToString());
            Assert.That(result.Score, Is.EqualTo(expectedScore));
        }

        [Test]
        public void Replay_OneDaubCompletesRowAndColumn_ClaimIsWorth1000()
        {
            for (uint seed = 1; seed < 500; seed++)
            {
                var game = Bingo.GenerateGame(seed);
                for (var row = 0; row < Rules.Size; row++)
                {
                    for (var column = 0; column < Rules.Size; column++)
                    {
                        if (TryAssertCrossClaim(game, row, column))
                            return;
                    }
                }
            }
            Assert.Fail("no seed has a fully called row and column");
        }

        [Test]
        public void Replay_FalseBingo_Costs200AndOnlyTheFinalScoreIsClamped()
        {
            var (cell, callTime) = OnCardCall();
            var clamped = Bingo.Replay(Seed, new[] { Move.DaubAt(cell, callTime + 200), Move.BingoAt(callTime + 1000) });
            Assert.That(clamped.Ok && clamped.Score == 0 && clamped.FalseBingos == 1, clamped.ToString());

            var (secondCell, secondCallTime) = OnCardCall(2);
            var result = Bingo.Replay(Seed, new[] { Move.BingoAt(100), Move.DaubAt(cell, callTime + 300), Move.DaubAt(secondCell, secondCallTime + 300) });
            Assert.That(result.Score, Is.EqualTo(147 + 147 - 200), "the running total may go negative mid-match");
        }

        [Test]
        public void Replay_UnknownKind_RejectsMalformedMove() =>
            Assert.That(RejectReasonOf(new Move { kind = "jump", cell = 1, t = 3000 }), Is.EqualTo(RejectReason.MalformedMove));

        [Test]
        public void Replay_OverMaxMoves_RejectsTooManyMoves() =>
            Assert.That(RejectReasonOf(Enumerable.Range(0, Rules.MaxMoves + 1).Select(i => Move.BingoAt(i * 200)).ToArray()),
                Is.EqualTo(RejectReason.TooManyMoves));

        [Test]
        public void Replay_MoveOutsideTheMatch_RejectsOutsideMatchTime()
        {
            Assert.That(RejectReasonOf(Move.BingoAt(-1)), Is.EqualTo(RejectReason.OutsideMatchTime));
            Assert.That(RejectReasonOf(Move.BingoAt(Rules.MatchDurationMs + 1)), Is.EqualTo(RejectReason.OutsideMatchTime));
        }

        [Test]
        public void Replay_MovesUnder150MsApart_RejectsImplausibleSpeed() =>
            Assert.That(RejectReasonOf(Move.BingoAt(1000), Move.BingoAt(1149)), Is.EqualTo(RejectReason.ImplausibleSpeed));

        [Test]
        public void Replay_CellOffTheCardOrFree_RejectsCellOutOfRange()
        {
            Assert.That(RejectReasonOf(Move.DaubAt(25, 3000)), Is.EqualTo(RejectReason.CellOutOfRange));
            Assert.That(RejectReasonOf(Move.DaubAt(-1, 3000)), Is.EqualTo(RejectReason.CellOutOfRange));
            Assert.That(RejectReasonOf(Move.DaubAt(Rules.FreeCell, 3000)), Is.EqualTo(RejectReason.CellOutOfRange));
        }

        [Test]
        public void Replay_SameCellTwice_RejectsCellAlreadyDaubed()
        {
            var (cell, callTime) = OnCardCall();
            Assert.That(RejectReasonOf(Move.DaubAt(cell, callTime + 300), Move.DaubAt(cell, callTime + 600)), Is.EqualTo(RejectReason.CellAlreadyDaubed));
        }

        [Test]
        public void Replay_NumberNotInTheCalls_RejectsNumberNeverCalled()
        {
            var cell = Enumerable.Range(0, Rules.CellCount).First(index => index != Rules.FreeCell && !_game.Calls.Contains(_game.Card[index]));
            Assert.That(RejectReasonOf(Move.DaubAt(cell, 5000)), Is.EqualTo(RejectReason.NumberNeverCalled));
        }

        [Test]
        public void Replay_DaubBeforeItsCall_RejectsDaubBeforeCall()
        {
            var (cell, callTime) = OnCardCall();
            Assert.That(RejectReasonOf(Move.DaubAt(cell, callTime - 1)), Is.EqualTo(RejectReason.DaubBeforeCall));
        }

        [Test]
        public void Replay_DaubUnder200MsAfterTheCall_RejectsImplausibleReaction()
        {
            var (cell, callTime) = OnCardCall();
            Assert.That(RejectReasonOf(Move.DaubAt(cell, callTime + 199)), Is.EqualTo(RejectReason.ImplausibleReaction));
            Assert.That(Bingo.Replay(Seed, new[] { Move.DaubAt(cell, callTime + 200) }).Ok);
        }

        // The ordinal-th call (from 1, skipping call 0) whose number is on the card.
        private (int cell, int callTime) OnCardCall(int ordinal = 1)
        {
            var seen = 0;
            for (var i = 1; i < _game.Calls.Length; i++)
            {
                var cell = Array.IndexOf(_game.Card, _game.Calls[i]);
                if (cell >= 0 && ++seen == ordinal)
                    return (cell, Rules.CallTimeMs(i));
            }
            throw new InvalidOperationException("not enough on-card calls");
        }

        private string RejectReasonOf(params Move[] moves)
        {
            var result = Bingo.Replay(Seed, moves);
            Assert.That(result.Ok, Is.False, "expected a reject");
            return result.Reason;
        }

        // False when the row or the column has a number this game never calls.
        private static bool TryAssertCrossClaim(BingoGame game, int row, int column)
        {
            var cells = Bingo.Lines[row].Concat(Bingo.Lines[Rules.Size + column]).Distinct().Where(cell => cell != Rules.FreeCell).ToList();
            if (!cells.All(cell => game.Calls.Contains(game.Card[cell])))
                return false;

            int CallTime(int cell) => Rules.CallTimeMs(Array.IndexOf(game.Calls, game.Card[cell]));
            var crossing = row * Rules.Size + column;
            var moves = cells.Where(cell => cell != crossing).OrderBy(CallTime).Select(cell => Move.DaubAt(cell, CallTime(cell) + 300)).ToList();
            moves.Add(Move.DaubAt(crossing, Math.Max(moves[moves.Count - 1].t, CallTime(crossing)) + 300));
            var withClaim = new List<Move>(moves) { Move.BingoAt(moves[moves.Count - 1].t + 300) };

            var before = Bingo.Replay(game, moves);
            var after = Bingo.Replay(game, withClaim);
            Assert.That(before.Ok && after.Ok, after.ToString());
            Assert.That(after.Lines, Is.EqualTo(2));
            Assert.That(after.Score - before.Score, Is.EqualTo(1000));

            var again = Bingo.Replay(game, new List<Move>(withClaim) { Move.BingoAt(withClaim[withClaim.Count - 1].t + 300) });
            Assert.That(again.FalseBingos, Is.EqualTo(1));
            Assert.That(again.Score, Is.EqualTo(after.Score - 200));
            return true;
        }
    }
}
