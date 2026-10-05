using System;
using System.IO;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using TournamentSandbox.Core;
using TournamentSandbox.Net;
using UnityEngine;

namespace TournamentSandbox.Game
{
    public sealed class OnlineMatchService
    {
        private const int PollIntervalMs = 2000;

        private readonly IApiClient _api;
        private readonly IServerClock _clock;
        private readonly IPendingSubmissionStore _store;
        private readonly RetryPolicy _retryPolicy;
        private readonly string _deviceId;
        private readonly SemaphoreSlim _loginLock = new SemaphoreSlim(1, 1);

        private PendingSubmission _pending;

        public OnlineMatchService(IApiClient api, IServerClock clock, IPendingSubmissionStore store, RetryPolicy retryPolicy, string deviceId)
        {
            _api = api ?? throw new ArgumentNullException(nameof(api));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _retryPolicy = retryPolicy ?? throw new ArgumentNullException(nameof(retryPolicy));
            _deviceId = deviceId;
        }

        // GetTimeAsync never retries, as a backoff would spoil its RTT sample: the whole sync is retried.
        public async UniTask EnsureClockSyncedAsync(CancellationToken cancellationToken)
        {
            for (var attempt = 1; !_clock.IsSynced; attempt++)
            {
                try
                {
                    await _clock.SyncAsync(cancellationToken);
                }
                catch (ApiException error) when (_retryPolicy.ShouldRetry(error, attempt))
                {
                    await UniTask.Delay(_retryPolicy.GetDelay(attempt), DelayType.Realtime, cancellationToken: cancellationToken);
                }
            }
        }

        // Each login rotates the token, so concurrent callers must share one.
        public async UniTask EnsureLoggedInAsync(CancellationToken cancellationToken)
        {
            if (_api.IsLoggedIn)
                return;

            await _loginLock.WaitAsync(cancellationToken);
            try
            {
                if (!_api.IsLoggedIn)
                {
                    await _api.LoginAsync(_deviceId, cancellationToken);
                }
            }
            finally
            {
                _loginLock.Release();
            }
        }

        public async UniTask<int> GetBalanceAsync(CancellationToken cancellationToken)
        {
            await EnsureLoggedInAsync(cancellationToken);
            var wallet = await _api.GetWalletAsync(cancellationToken);
            return wallet.balance;
        }

        public async UniTask<MatchView> EnterAsync(string tournamentId, CancellationToken cancellationToken)
        {
            await EnsureLoggedInAsync(cancellationToken);

            // One key per tap on Play; ApiClient reuses it on every retry.
            var idempotencyKey = Guid.NewGuid().ToString("N");
            var match = await _api.EnterAsync(tournamentId, idempotencyKey, cancellationToken);

            _pending = new PendingSubmission
            {
                matchId = match.matchId,
                tournamentId = match.tournamentId,
                seed = match.seed,
                startedAtServerMs = match.startedAt,
                deadlineServerMs = match.deadline,
                moves = Array.Empty<Move>(),
            };
            Save();
            return match;
        }

        public void SaveMoves(MatchRecorder recorder)
        {
            _pending.moves = recorder.Moves.ToArray();
            _pending.clientScore = recorder.Prediction.Score;
            Save();
        }

        // Sends the saved copy, so a resubmit after a relaunch has the same hash.
        public async UniTask<MatchView> SubmitAsync(CancellationToken cancellationToken)
        {
            var request = new SubmitRequest
            {
                moves = _pending.moves,
                clientScore = _pending.clientScore,
            };

            try
            {
                var submitted = await _api.SubmitAsync(_pending.matchId, request, cancellationToken);
                Clear();
                return submitted;
            }
            catch (ApiException exception) when (exception.IsClientError)
            {
                Clear();
                throw;
            }
        }

        // Null when nothing is pending.
        public async UniTask<ResumedMatch> TryResumeAsync(CancellationToken cancellationToken)
        {
            if (!_store.TryLoad(out var pending))
                return null;

            try
            {
                await EnsureLoggedInAsync(cancellationToken);
                await EnsureClockSyncedAsync(cancellationToken);

                var match = await _api.GetMatchAsync(pending.matchId, cancellationToken);
                if (match.status == MatchStatus.Playing)
                {
                    _pending = pending;
                }
                else
                {
                    Clear();
                }

                return new ResumedMatch(match, pending.moves, _clock.ServerNowMs - pending.startedAtServerMs);
            }
            catch (ApiException exception) when (exception.IsClientError)
            {
                Clear();
                throw;
            }
        }

        public async UniTask<MatchView> WaitForResultAsync(MatchView match, CancellationToken cancellationToken)
        {
            while (!match.IsFinal)
            {
                await UniTask.Delay(PollIntervalMs, cancellationToken: cancellationToken);
                match = await _api.GetMatchAsync(match.matchId, cancellationToken);
            }
            return match;
        }

        private void Save()
        {
            try
            {
                _store.Save(_pending);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                Debug.LogWarning($"[online] could not save the match: {exception.Message}");
            }
        }

        private void Clear()
        {
            try
            {
                _store.Clear();
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                Debug.LogWarning($"[online] could not delete the saved match: {exception.Message}");
            }
        }
    }
}
