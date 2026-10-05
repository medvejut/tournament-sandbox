using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using TournamentSandbox.Net;
using UnityEngine.TestTools;

namespace TournamentSandbox.Tests
{
    public sealed class ServerClockTests
    {
        private sealed class FakeTime
        {
            public double Seconds { get; set; }
        }

        // Answers /v1/time with ServerTime after advancing local time by RttSeconds, or throws Error.
        private sealed class FakeApi : IApiClient
        {
            private readonly FakeTime _time;

            public long ServerTime { get; set; }
            public double RttSeconds { get; set; }
            public Exception Error { get; set; }

            public FakeApi(FakeTime time)
            {
                _time = time;
            }

            public string AuthToken => null;
            public bool IsLoggedIn => false;

            public event Action<ApiRetry> Retrying
            {
                add { }
                remove { }
            }

            public UniTask<TimeResponse> GetTimeAsync(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _time.Seconds += RttSeconds;
                if (Error != null)
                    throw Error;
                return UniTask.FromResult(new TimeResponse { serverTime = ServerTime });
            }

            public UniTask<LoginResponse> LoginAsync(string deviceId, CancellationToken cancellationToken) => throw new NotSupportedException();
            public UniTask<WalletResponse> GetWalletAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
            public UniTask<MatchView> EnterAsync(string tournamentId, string idempotencyKey, CancellationToken cancellationToken) => throw new NotSupportedException();
            public UniTask<MatchView> SubmitAsync(string matchId, SubmitRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
            public UniTask<MatchView> GetMatchAsync(string matchId, CancellationToken cancellationToken) => throw new NotSupportedException();
            public UniTask<LiveOpsResponse> GetLiveOpsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        }

        private FakeTime _time;
        private FakeApi _api;
        private ServerClock _clock;

        [SetUp]
        public void SetUp()
        {
            _time = new FakeTime { Seconds = 100.0 };
            _api = new FakeApi(_time) { ServerTime = 1_800_000_000_000, RttSeconds = 0.2 };
            _clock = new ServerClock(_api, () => _time.Seconds);
        }

        [Test]
        public void ServerNowMs_BeforeSync_Throws()
        {
            Assert.That(_clock.IsSynced, Is.False);
            Assert.Throws<InvalidOperationException>(() => _ = _clock.ServerNowMs);
        }

        [UnityTest]
        public IEnumerator SyncAsync_CorrectsForHalfTheRoundTrip() => UniTask.ToCoroutine(async () =>
        {
            await _clock.SyncAsync(CancellationToken.None);

            Assert.That(_clock.IsSynced, Is.True);
            Assert.That(_clock.LastRttMs, Is.EqualTo(200).Within(0.001));
            // The server stamped its time at the midpoint (local 100.1 s); it's now local 100.2 s.
            Assert.That(_clock.ServerNowMs, Is.EqualTo(1_800_000_000_100));
        });

        [UnityTest]
        public IEnumerator ServerNowMs_AdvancesWithTheMonotonicClock() => UniTask.ToCoroutine(async () =>
        {
            await _clock.SyncAsync(CancellationToken.None);
            var before = _clock.ServerNowMs;
            _time.Seconds += 5.5;
            Assert.That(_clock.ServerNowMs - before, Is.EqualTo(5500));
        });

        [UnityTest]
        public IEnumerator ServerNowMs_RealTimePasses_DoesNotMove() => UniTask.ToCoroutine(async () =>
        {
            // Only the injected clock may move it; moving here means a wall clock is being read.
            await _clock.SyncAsync(CancellationToken.None);
            var before = _clock.ServerNowMs;
            Thread.Sleep(30);
            Assert.That(_clock.ServerNowMs, Is.EqualTo(before));
        });

        [UnityTest]
        public IEnumerator SyncAsync_Again_ReplacesTheOffset() => UniTask.ToCoroutine(async () =>
        {
            await _clock.SyncAsync(CancellationToken.None);

            // Backgrounded: the server moved on 60 s, the monotonic clock only 1 s.
            _time.Seconds += 1.0;
            _api.ServerTime += 60_000;
            _api.RttSeconds = 0.1;
            await _clock.SyncAsync(CancellationToken.None);

            Assert.That(_clock.LastRttMs, Is.EqualTo(100).Within(0.001));
            Assert.That(_clock.ServerNowMs, Is.EqualTo(1_800_000_060_050));
        });

        [UnityTest]
        public IEnumerator SyncAsync_Failure_PropagatesAndKeepsThePreviousOffset() => UniTask.ToCoroutine(async () =>
        {
            await _clock.SyncAsync(CancellationToken.None);
            var before = _clock.ServerNowMs;

            _api.Error = ApiException.Timeout();
            _api.RttSeconds = 0;
            var timedOut = false;
            try
            {
                await _clock.SyncAsync(CancellationToken.None);
            }
            catch (ApiException error)
            {
                timedOut = error.Code == ErrorCodes.Timeout;
            }

            Assert.That(timedOut, Is.True, "the ApiException reaches the caller");
            Assert.That(_clock.IsSynced, Is.True);
            Assert.That(_clock.ServerNowMs, Is.EqualTo(before));
        });

        [UnityTest]
        public IEnumerator SyncAsync_FirstSyncFails_StaysUnsynced() => UniTask.ToCoroutine(async () =>
        {
            _api.Error = ApiException.Network("down");
            var failed = false;
            try
            {
                await _clock.SyncAsync(CancellationToken.None);
            }
            catch (ApiException)
            {
                failed = true;
            }
            Assert.That(failed, Is.True);
            Assert.That(_clock.IsSynced, Is.False);
        });

        [UnityTest]
        public IEnumerator SyncAsync_Cancelled_PropagatesCancellation() => UniTask.ToCoroutine(async () =>
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var cancelled = false;
            try
            {
                await _clock.SyncAsync(cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }
            Assert.That(cancelled, Is.True);
            Assert.That(_clock.IsSynced, Is.False);
        });
    }
}
