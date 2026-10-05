using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TournamentSandbox.Core;
using TournamentSandbox.Game;

namespace TournamentSandbox.Tests
{
    // The key property: anything MatchRecorder accepts, the server's replay accepts too.
    public sealed class GameTests
    {
        private const uint Seed = 20261004;
        private BingoGame _game;
        private CallScheduler _calls;

        [SetUp]
        public void SetUp()
        {
            _game = Bingo.GenerateGame(Seed);
            _calls = new CallScheduler(_game);
        }

        [Test]
        public void CallIndexAt_FollowsTheClock()
        {
            Assert.That(_calls.CallIndexAt(-1), Is.EqualTo(-1));
            Assert.That(_calls.CallIndexAt(0), Is.EqualTo(0));
            Assert.That(_calls.CallIndexAt(1999), Is.EqualTo(0));
            Assert.That(_calls.CallIndexAt(2000), Is.EqualTo(1));
            Assert.That(_calls.CallIndexAt(118_000), Is.EqualTo(59));
            Assert.That(_calls.CallIndexAt(500_000), Is.EqualTo(59), "capped after the last call");
        }

        [Test]
        public void CallIndexAt_AfterAFreeze_EqualsSteppingFrameByFrame()
        {
            var stepped = -1;
            for (var elapsedMs = 0; elapsedMs <= 61_000; elapsedMs += 16)
            {
                stepped = _calls.CallIndexAt(elapsedMs);
            }
            Assert.That(_calls.CallIndexAt(61_000), Is.EqualTo(stepped));
        }

        [Test]
        public void IsCalled_FromTheCallTimeOn()
        {
            Assert.That(_calls.CurrentNumber(-500), Is.EqualTo(0));
            Assert.That(_calls.CurrentNumber(4100), Is.EqualTo(_game.Calls[2]));
            Assert.That(_calls.IsCalled(_game.Calls[2], 3999), Is.False);
            Assert.That(_calls.IsCalled(_game.Calls[2], 4000), Is.True);
            var neverCalled = Enumerable.Range(1, 75).First(number => !_game.Calls.Contains(number));
            Assert.That(_calls.CallTimeOf(neverCalled), Is.EqualTo(-1));
            Assert.That(_calls.IsCalled(neverCalled, Rules.MatchDurationMs), Is.False);
        }

        [Test]
        public void MsUntilNextCall_CountsDownAndEndsAtMinusOne()
        {
            Assert.That(_calls.MsUntilNextCall(0), Is.EqualTo(2000));
            Assert.That(_calls.MsUntilNextCall(2500), Is.EqualTo(1500));
            Assert.That(_calls.MsUntilNextCall(118_000), Is.EqualTo(-1));
        }

        [Test]
        public void PreviousNumbers_AreNewestFirstWithoutTheCurrentOne()
        {
            var numbers = new List<int>();
            _calls.PreviousNumbers(10_000, numbers, 4);
            Assert.That(numbers, Is.EqualTo(new[] { _game.Calls[4], _game.Calls[3], _game.Calls[2], _game.Calls[1] }));
            _calls.PreviousNumbers(2000, numbers, 4);
            Assert.That(numbers, Is.EqualTo(new[] { _game.Calls[0] }));
        }

        [TestCase(1, 'B')]
        [TestCase(15, 'B')]
        [TestCase(16, 'I')]
        [TestCase(45, 'N')]
        [TestCase(60, 'G')]
        [TestCase(75, 'O')]
        public void ColumnLetter_IsTheNumbersColumn(int number, char letter) =>
            Assert.That(CallScheduler.ColumnLetter(number), Is.EqualTo(letter));

        [Test]
        public void TryDaub_CalledNumber_RecordsMoveAndUpdatesPrediction()
        {
            var recorder = new MatchRecorder(_game);
            var (cell, callTime) = OnCardCall();
            Assert.That(recorder.TryDaub(cell, callTime + 200), Is.EqualTo(TapResult.Accepted));
            Assert.That(recorder.IsDaubed(cell));
            Assert.That(recorder.Prediction.Score, Is.EqualTo(148));
            Assert.That(recorder.Moves.Count, Is.EqualTo(1));
        }

        [Test]
        public void TryDaub_WhatTheServerWouldReject_IsRefusedAndNotRecorded()
        {
            var recorder = new MatchRecorder(_game);
            var (cell, callTime) = OnCardCall();
            var (secondCell, secondCallTime) = OnCardCall(2);
            Assert.That(recorder.TryDaub(cell, callTime - 1), Is.EqualTo(TapResult.NotCalledYet));
            Assert.That(recorder.TryDaub(cell, callTime + 199), Is.EqualTo(TapResult.TooSoonAfterCall));
            Assert.That(recorder.TryDaub(Rules.FreeCell, callTime + 500), Is.EqualTo(TapResult.FreeCell));
            Assert.That(recorder.TryDaub(cell, secondCallTime + 300), Is.EqualTo(TapResult.Accepted));
            Assert.That(recorder.TryDaub(cell, secondCallTime + 600), Is.EqualTo(TapResult.AlreadyDaubed));
            Assert.That(recorder.TryDaub(secondCell, secondCallTime + 300 + 149), Is.EqualTo(TapResult.TooFastAfterLastMove));
            Assert.That(recorder.TryDaub(secondCell, Rules.MatchDurationMs + 1), Is.EqualTo(TapResult.OutOfTime));
            Assert.That(recorder.Moves.Count, Is.EqualTo(1));
        }

        [Test]
        public void TryClaimBingo_NoLine_IsRecordedAsAFalseBingo()
        {
            var recorder = new MatchRecorder(_game);
            Assert.That(recorder.TryClaimBingo(1000, out var newLines), Is.EqualTo(TapResult.Accepted));
            Assert.That(newLines, Is.EqualTo(0));
            Assert.That(recorder.Prediction.FalseBingos, Is.EqualTo(1));
        }

        [Test]
        public void TryClaimBingo_AtMaxMoves_ReturnsMoveLimitReached()
        {
            var recorder = new MatchRecorder(_game);
            for (var i = 0; i < Rules.MaxMoves; i++)
            {
                Assert.That(recorder.TryClaimBingo(i * 200, out _), Is.EqualTo(TapResult.Accepted));
            }
            Assert.That(recorder.TryClaimBingo(Rules.MaxMoves * 200, out _), Is.EqualTo(TapResult.MoveLimitReached));
        }

        [Test]
        public void Moves_FromRandomTapping_AreAlwaysAcceptedByTheServerReplay()
        {
            var random = new Random(7);
            for (var round = 0; round < 200; round++)
            {
                var recorder = new MatchRecorder(Bingo.GenerateGame((uint)random.Next()));
                for (var elapsedMs = 0; elapsedMs <= Rules.MatchDurationMs; elapsedMs += random.Next(0, 900))
                {
                    if (random.Next(6) == 0)
                    {
                        recorder.TryClaimBingo(elapsedMs, out _);
                    }
                    else
                    {
                        recorder.TryDaub(random.Next(Rules.CellCount), elapsedMs);
                    }
                }
                var replay = Bingo.Replay(recorder.Game, recorder.Moves);
                Assert.That(replay.Ok, $"round {round}: {replay}");
                Assert.That(replay.Score, Is.EqualTo(recorder.Prediction.Score));
            }
        }

        [Test]
        public void TryClaimBingo_AfterAFullRow_ClaimsIt_AndARebuildFromSavedMovesMatches()
        {
            for (uint seed = 1; seed < 300; seed++)
            {
                var game = Bingo.GenerateGame(seed);
                for (var row = 0; row < Rules.Size; row++)
                {
                    if (TryAssertRowClaim(game, row))
                        return;
                }
            }
            Assert.Fail("no seed has a fully called row");
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

        // False when the row has a number this game never calls.
        private static bool TryAssertRowClaim(BingoGame game, int row)
        {
            var cells = Bingo.Lines[row].Where(cell => cell != Rules.FreeCell).ToArray();
            if (!cells.All(cell => game.Calls.Contains(game.Card[cell])))
                return false;

            var calls = new CallScheduler(game);
            var recorder = new MatchRecorder(game);
            foreach (var cell in cells.OrderBy(cell => calls.CallTimeOf(game.Card[cell])))
            {
                Assert.That(recorder.TryDaub(cell, calls.CallTimeOf(game.Card[cell]) + 400), Is.EqualTo(TapResult.Accepted));
            }
            Assert.That(recorder.IsLineClaimed(row), Is.False, "completing a line doesn't claim it");
            Assert.That(recorder.TryClaimBingo(recorder.LastMoveT + 500, out var newLines), Is.EqualTo(TapResult.Accepted));
            Assert.That(newLines, Is.EqualTo(1));
            Assert.That(recorder.IsLineClaimed(row));

            var rebuilt = new MatchRecorder(game, recorder.Moves);
            Assert.That(rebuilt.Prediction.Score, Is.EqualTo(recorder.Prediction.Score));
            Assert.That(rebuilt.IsLineClaimed(row));
            return true;
        }
    }
}
