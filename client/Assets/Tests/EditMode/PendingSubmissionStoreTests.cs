using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using TournamentSandbox.Core;
using TournamentSandbox.Net;

namespace TournamentSandbox.Tests
{
    public sealed class PendingSubmissionStoreTests
    {
        private string _directory;
        private List<string> _warnings;

        private string MainFile => Path.Combine(_directory, PendingSubmissionStore.FileName);
        private string TempFile => MainFile + PendingSubmissionStore.TempSuffix;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "ts-store-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            _warnings = new List<string>();
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }

        [Test]
        public void FilePath_IsInTheGivenDirectory()
        {
            var store = Store();
            Assert.That(store.FilePath, Is.EqualTo(MainFile));
            Assert.That(store.TempPath, Is.EqualTo(TempFile));
        }

        [Test]
        public void Constructor_DoesNotTouchTheDisk()
        {
            var missing = Path.Combine(_directory, "not-yet");
            Store(missing);
            Assert.That(Directory.Exists(missing), Is.False);
        }

        [Test]
        public void TryLoad_NothingSaved_ReturnsFalseWithoutWarning()
        {
            Assert.That(Store().TryLoad(out var loaded), Is.False);
            Assert.That(loaded, Is.Null);
            Assert.That(_warnings, Is.Empty);
        }

        [Test]
        public void TryLoad_AfterSave_RoundTripsEveryField()
        {
            var original = Sample(moveCount: 64);
            Store().Save(original);

            Assert.That(Store().TryLoad(out var loaded), Is.True, "a fresh instance must read what another wrote");
            Assert.That(loaded.matchId, Is.EqualTo(original.matchId));
            Assert.That(loaded.tournamentId, Is.EqualTo(original.tournamentId));
            Assert.That(loaded.seed, Is.EqualTo(original.seed));
            Assert.That(loaded.Seed, Is.EqualTo(uint.MaxValue));
            Assert.That(loaded.startedAtServerMs, Is.EqualTo(original.startedAtServerMs));
            Assert.That(loaded.deadlineServerMs, Is.EqualTo(original.deadlineServerMs));
            Assert.That(loaded.clientScore, Is.EqualTo(original.clientScore));
            Assert.That(loaded.moves, Is.EqualTo(original.moves));
            Assert.That(_warnings, Is.Empty);
        }

        [Test]
        public void Save_EmptyMoveLog_IsValid()
        {
            Store().Save(Sample(moveCount: 0));
            Assert.That(Store().TryLoad(out var loaded), Is.True);
            Assert.That(loaded.moves, Is.Empty);
        }

        [Test]
        public void Save_Again_OverwritesThePreviousSubmission()
        {
            var store = Store();
            store.Save(Sample("m-1", 2));
            store.Save(Sample("m-1", 5));
            store.Save(Sample("m-2", 1));
            Assert.That(store.TryLoad(out var loaded), Is.True);
            Assert.That(loaded.matchId, Is.EqualTo("m-2"));
            Assert.That(loaded.moves.Length, Is.EqualTo(1));
        }

        [Test]
        public void Save_LeavesNoTempFileBehind()
        {
            var store = Store();
            store.Save(Sample());
            store.Save(Sample(moveCount: 10)); // the second save takes the replace path
            Assert.That(File.Exists(MainFile), Is.True);
            Assert.That(File.Exists(TempFile), Is.False);
            Assert.That(Directory.GetFiles(_directory), Has.Length.EqualTo(1));
        }

        [Test]
        public void Save_MissingDirectory_CreatesIt()
        {
            var store = Store(Path.Combine(_directory, "a", "b"));
            store.Save(Sample());
            Assert.That(store.TryLoad(out _), Is.True);
        }

        [Test]
        public void Save_StaleTempFileFromACrash_IsOverwritten()
        {
            File.WriteAllText(TempFile, "{\"matchId\":\"half-writ");
            var store = Store();
            store.Save(Sample("fresh"));
            Assert.That(store.TryLoad(out var loaded), Is.True);
            Assert.That(loaded.matchId, Is.EqualTo("fresh"));
            Assert.That(File.Exists(TempFile), Is.False);
        }

        [Test]
        public void TryLoad_TornTempNextToACompleteFile_ReturnsTheCompleteOne()
        {
            var store = Store();
            store.Save(Sample("complete"));
            File.WriteAllText(TempFile, "{\"matchId\":\"torn"); // a crash between "write temp" and "replace"
            Assert.That(store.TryLoad(out var loaded), Is.True);
            Assert.That(loaded.matchId, Is.EqualTo("complete"));
            Assert.That(_warnings, Is.Empty);
        }

        [Test]
        public void TryLoad_OnlyATempFile_MeansNothingPending()
        {
            File.WriteAllText(TempFile, "{\"matchId\":\"torn");
            Assert.That(Store().TryLoad(out _), Is.False);
            Assert.That(_warnings, Is.Empty);
        }

        [TestCase("")]
        [TestCase("   ")]
        [TestCase("not json at all")]
        [TestCase("{\"matchId\":\"m-1\",\"moves\":[{\"kind\":\"daub\",")]
        [TestCase("{}")]
        [TestCase("{\"matchId\":\"\",\"moves\":[]}")]
        public void TryLoad_CorruptFile_WarnsOnceDeletesItAndReturnsFalse(string contents)
        {
            File.WriteAllText(MainFile, contents);
            var store = Store();

            var loadedSomething = true;
            PendingSubmission loaded = null;
            Assert.DoesNotThrow(() => loadedSomething = store.TryLoad(out loaded));
            Assert.That(loadedSomething, Is.False);
            Assert.That(loaded, Is.Null);
            Assert.That(_warnings, Has.Count.EqualTo(1));
            Assert.That(File.Exists(MainFile), Is.False, "a corrupt file is deleted so the next boot is clean");

            Assert.That(store.TryLoad(out _), Is.False);
            Assert.That(_warnings, Has.Count.EqualTo(1), "a second load finds nothing and doesn't warn again");
        }

        [Test]
        public void TryLoad_BinaryGarbage_DoesNotThrow()
        {
            File.WriteAllBytes(MainFile, new byte[] { 0xFF, 0xFE, 0x00, 0x01, 0x80, 0x7B });
            Assert.That(Store().TryLoad(out _), Is.False);
            Assert.That(_warnings, Has.Count.EqualTo(1));
        }

        [Test]
        public void Clear_RemovesThePendingSubmission()
        {
            var store = Store();
            store.Save(Sample());
            store.Clear();
            Assert.That(store.TryLoad(out _), Is.False);
            Assert.That(File.Exists(MainFile), Is.False);
        }

        [Test]
        public void Clear_AlsoRemovesAStaleTempFile()
        {
            File.WriteAllText(TempFile, "torn");
            Store().Clear();
            Assert.That(File.Exists(TempFile), Is.False);
        }

        [Test]
        public void Clear_NothingStored_IsANoOp()
        {
            Assert.DoesNotThrow(() => Store().Clear());
            Assert.DoesNotThrow(() => Store(Path.Combine(_directory, "missing-directory")).Clear());
        }

        [Test]
        public void Save_InvalidSubmission_ThrowsAndWritesNothing()
        {
            var store = Store();
            Assert.Throws<ArgumentException>(() => store.Save(null));
            Assert.Throws<ArgumentException>(() => store.Save(new PendingSubmission { matchId = "", moves = new Move[0] }));
            Assert.Throws<ArgumentException>(() => store.Save(new PendingSubmission { matchId = "m", moves = null }));
            Assert.That(File.Exists(MainFile), Is.False);
        }

        private PendingSubmissionStore Store(string directory = null) => new PendingSubmissionStore(directory ?? _directory, _warnings.Add);

        private static PendingSubmission Sample(string matchId = "m-1", int moveCount = 3) => new PendingSubmission
        {
            matchId = matchId,
            tournamentId = "bronze",
            seed = uint.MaxValue,
            startedAtServerMs = 1_791_126_986_294,
            deadlineServerMs = 1_791_127_706_294,
            moves = SampleMoves(moveCount),
            clientScore = 1234,
        };

        // Only the round trip matters here, not whether the moves are legal.
        private static Move[] SampleMoves(int count)
        {
            var moves = new Move[count];
            for (var i = 0; i < count; i++)
            {
                var t = 1000 + i * 2000;
                moves[i] = i % 4 == 3 ? Move.BingoAt(t) : Move.DaubAt(i % Rules.CellCount, t);
            }
            return moves;
        }
    }
}
