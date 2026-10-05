// Asserts Core against spec/vectors.json, the file the server tests use too.

using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TournamentSandbox.Core;
using UnityEngine;

namespace TournamentSandbox.Tests
{
    public sealed class VectorsTests
    {
#pragma warning disable 0649 // fields are assigned by JsonUtility
        [Serializable]
        private sealed class NextUIntVector
        {
            public long seed;
            public long[] values;
        }

        [Serializable]
        private sealed class NextVector
        {
            public long seed;
            public long n;
            public long[] values;
        }

        [Serializable]
        private sealed class GameVector
        {
            public long seed;
            public int[] card;
            public int[] calls;
        }

        [Serializable]
        private sealed class ExpectedReplay
        {
            public bool ok;
            public int score;
            public int daubs;
            public int lines;
            public int falseBingos;
            public string reason;
            public int moveIndex;
        }

        [Serializable]
        private sealed class ReplayVector
        {
            public string name;
            public long seed;
            public Move[] moves;
            public ExpectedReplay expected;
        }

        [Serializable]
        private sealed class Vectors
        {
            public int version;
            public NextUIntVector[] nextUInt;
            public NextVector[] next;
            public GameVector[] games;
            public ReplayVector[] replays;
        }
#pragma warning restore 0649

        private Vectors _vectors;

        [OneTimeSetUp]
        public void LoadVectors()
        {
            var path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "spec", "vectors.json"));
            Assert.That(File.Exists(path), $"missing {path}");
            _vectors = JsonUtility.FromJson<Vectors>(File.ReadAllText(path));
            Assert.That(_vectors.version, Is.EqualTo(1));
            Assert.That(_vectors.nextUInt, Is.Not.Empty);
            Assert.That(_vectors.next, Is.Not.Empty);
            Assert.That(_vectors.games, Is.Not.Empty);
            Assert.That(_vectors.replays, Is.Not.Empty);
        }

        [Test]
        public void NextUInt_MatchesVectors()
        {
            foreach (var vector in _vectors.nextUInt)
            {
                var random = new XorShift32((uint)vector.seed);
                var actual = vector.values.Select(_ => (long)random.NextUInt()).ToArray();
                Assert.That(actual, Is.EqualTo(vector.values), $"seed {vector.seed}");
            }
        }

        [Test]
        public void Next_IncludingARejectionHeavyBound_MatchesVectors()
        {
            Assert.That(_vectors.next.Any(vector => vector.n > int.MaxValue), "vectors should include a bound that forces rejections");
            foreach (var vector in _vectors.next)
            {
                var random = new XorShift32((uint)vector.seed);
                var actual = vector.values.Select(_ => (long)random.Next((ulong)vector.n)).ToArray();
                Assert.That(actual, Is.EqualTo(vector.values), $"n {vector.n}");
            }
        }

        [Test]
        public void GenerateGame_MatchesVectors()
        {
            foreach (var vector in _vectors.games)
            {
                var game = Bingo.GenerateGame((uint)vector.seed);
                Assert.That(game.Card, Is.EqualTo(vector.card), $"card, seed {vector.seed}");
                Assert.That(game.Calls, Is.EqualTo(vector.calls), $"calls, seed {vector.seed}");
            }
        }

        [Test]
        public void Replay_MatchesVectors()
        {
            foreach (var vector in _vectors.replays)
            {
                var result = Bingo.Replay((uint)vector.seed, vector.moves);
                var expected = vector.expected;
                Assert.That(result.Ok, Is.EqualTo(expected.ok), $"{vector.name}: {result}");
                if (expected.ok)
                {
                    Assert.That(result.Score, Is.EqualTo(expected.score), vector.name);
                    Assert.That(result.Daubs, Is.EqualTo(expected.daubs), vector.name);
                    Assert.That(result.Lines, Is.EqualTo(expected.lines), vector.name);
                    Assert.That(result.FalseBingos, Is.EqualTo(expected.falseBingos), vector.name);
                }
                else
                {
                    Assert.That(result.Reason, Is.EqualTo(expected.reason), vector.name);
                    Assert.That(result.MoveIndex, Is.EqualTo(expected.moveIndex), vector.name);
                }
            }
        }
    }
}
