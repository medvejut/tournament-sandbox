using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using TournamentSandbox.Core;
using TournamentSandbox.Game;
using TournamentSandbox.Net;
using TournamentSandbox.UI;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace TournamentSandbox.Bootstrap
{
    public sealed class MatchFlow : MonoBehaviour
    {
        [Header("Screens")]
        [SerializeField] private LobbyView lobby;
        [SerializeField] private GameObject matchScreen;
        [SerializeField] private MatchResultView result;

        [Header("Match")]
        [SerializeField] private MatchCardView card;
        [SerializeField] private MatchCallsView calls;
        [SerializeField] private MatchHudView hud;
        [SerializeField] private Button bingoButton;

        [Header("Online")]
        [SerializeField] private string tournamentId = "bronze";

        [Header("Tuning")]
        [Min(0)]
        [SerializeField] private int countdownMs = 3000;
        [Tooltip("A daub with at least this speed bonus gets the \"good\" toast colour.")]
        [Range(0, Rules.MaxSpeedBonus)]
        [SerializeField] private int goodSpeedBonus = 30;

        private IApiClient _api;
        private OnlineMatchService _online;
        private ApiClientOptions _server;
        private MatchPresenter _play;
        private ResultPresenter _results;
        private CancellationTokenSource _onlineCancellation;
        private MatchView _match; // null in practice
        private uint _seed;

        private bool IsOnline => _match != null;

        [Inject]
        private void Construct(IApiClient api, OnlineMatchService online, ApiClientOptions server)
        {
            _api = api;
            _online = online;
            _server = server;
        }

        private void Awake()
        {
            _play = new MatchPresenter(card, calls, hud, bingoButton, goodSpeedBonus);
            _play.MovesChanged += OnMovesChanged;
            _play.Finished += OnMatchFinished;
            _results = new ResultPresenter(result);

            _api.Retrying += ShowRetry;
            lobby.PracticeClicked += StartPractice;
            lobby.OnlineClicked += StartOnline;
            hud.QuitClicked += OnQuit;
            result.PlayAgainClicked += PlayAgain;
            result.LobbyClicked += ShowLobby;
            ShowLobby();
            ResumeOnlineAsync(RestartOnlineCancellation()).Forget();
        }

        private void OnDestroy() => _api.Retrying -= ShowRetry;

        private void Update() => _play.Tick();

        private void ShowLobby()
        {
            CancelOnline();
            _play.Abandon();
            lobby.SetBusy(false);
            lobby.ShowOnlineStatus($"Online: {tournamentId} tournament at {_server.baseUrl}");
            Show(lobby.gameObject);
            RefreshBalanceAsync(this.GetCancellationTokenOnDestroy()).Forget();
        }

        private async UniTask RefreshBalanceAsync(CancellationToken cancellationToken)
        {
            try
            {
                lobby.ShowBalance(await _online.GetBalanceAsync(cancellationToken));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                lobby.ShowBalance(null);
                OnlineLog.Report(exception);
            }
        }

        private void Show(GameObject screen)
        {
            lobby.gameObject.SetActive(screen == lobby.gameObject);
            matchScreen.SetActive(screen == matchScreen);
            result.gameObject.SetActive(screen == result.gameObject);
        }

        private void PlayAgain()
        {
            if (!IsOnline)
            {
                StartPractice();
                return;
            }

            ShowLobby();
            StartOnline();
        }

        private void OnQuit()
        {
            // Left unsubmitted, the match would be forfeited with 0 after the grace period.
            if (IsOnline)
            {
                _play.Finish();
                return;
            }

            ShowLobby();
        }

        private void StartPractice() =>
            StartMatch(BitConverter.ToUInt32(Guid.NewGuid().ToByteArray(), 0), null, Array.Empty<Move>(), -countdownMs);

        private void StartMatch(uint seed, MatchView match, IReadOnlyList<Move> savedMoves, int elapsedMs)
        {
            Show(matchScreen);
            _seed = seed;
            _match = match;
            _play.Start(Bingo.GenerateGame(seed), savedMoves, elapsedMs);
            Debug.Log(IsOnline ? $"[online] match {match.matchId}, seed {seed}, at {elapsedMs} ms" : $"[practice] seed {seed}");
        }

        private void OnMovesChanged(MatchRecorder recorder)
        {
            if (IsOnline)
            {
                _online.SaveMoves(recorder);
            }
        }

        private void OnMatchFinished(MatchRecorder recorder)
        {
            var prediction = recorder.Prediction;
            Debug.Log($"[{(IsOnline ? "online" : "practice")}] finished: {prediction}, {recorder.Moves.Count} moves");
            if (IsOnline)
            {
                SubmitOnlineAsync(_match, recorder, RestartOnlineCancellation()).Forget();
                return;
            }

            _results.ShowPractice(prediction, _seed);
            Show(result.gameObject);
        }

        private void ShowRetry(ApiRetry retry)
        {
            var message = $"{retry.Error.Message}. Retry {retry.FailedAttempt} in {retry.Delay.TotalSeconds:0.0}s...";
            if (lobby.gameObject.activeSelf)
            {
                lobby.ShowOnlineStatus(message);
            }
            else if (result.gameObject.activeSelf)
            {
                _results.ShowNote(message);
            }
        }

        private void StartOnline() => EnterOnlineAsync(RestartOnlineCancellation()).Forget();

        private async UniTask EnterOnlineAsync(CancellationToken cancellationToken)
        {
            lobby.SetBusy(true);
            lobby.ShowOnlineStatus("Connecting...");
            try
            {
                var match = await _online.EnterAsync(tournamentId, cancellationToken);
                // t counts from after the server's start, so moves stay within its timing bound.
                StartMatch(match.Seed, match, Array.Empty<Move>(), -countdownMs);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                lobby.ShowOnlineStatus(exception.Message);
                OnlineLog.Report(exception);
            }
            finally
            {
                lobby.SetBusy(false);
            }
        }

        private async UniTask SubmitOnlineAsync(MatchView match, MatchRecorder recorder, CancellationToken cancellationToken)
        {
            var prediction = recorder.Prediction;
            _results.ShowSubmitting(match, prediction);
            Show(result.gameObject);
            MatchView submitted;
            try
            {
                submitted = await _online.SubmitAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                var kept = exception is ApiException { IsClientError: true } ? "" : "\nThe moves are kept and sent on the next launch.";
                _results.ShowSubmitFailed(match, prediction, exception.Message + kept);
                OnlineLog.Report(exception);
                return;
            }

            await ShowResultAsync(submitted, prediction, cancellationToken);
        }

        private async UniTask ShowResultAsync(MatchView submitted, ReplayResult prediction, CancellationToken cancellationToken)
        {
            Show(result.gameObject);
            if (!submitted.IsFinal)
            {
                _results.ShowWaiting(submitted, prediction);
            }

            try
            {
                var final = await _online.WaitForResultAsync(submitted, cancellationToken);
                _results.ShowFinal(final, prediction);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _results.ShowNote(exception.Message);
                OnlineLog.Report(exception);
            }
        }

        private async UniTask ResumeOnlineAsync(CancellationToken cancellationToken)
        {
            lobby.SetBusy(true);
            try
            {
                var resumed = await _online.TryResumeAsync(cancellationToken);
                if (resumed == null)
                    return;

                var match = resumed.Match;
                if (!resumed.IsPlaying)
                {
                    _match = match;
                    await ShowResultAsync(match, Bingo.Replay(match.Seed, resumed.Moves), cancellationToken);
                    return;
                }

                // t = 0 is the end of the countdown.
                var elapsedMs = (int)Math.Min(resumed.ServerElapsedMs - countdownMs, Rules.MatchDurationMs);
                StartMatch(match.Seed, match, resumed.Moves, elapsedMs);
                hud.Toast("match resumed", ToastTone.Highlight, 1.6f);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                lobby.ShowOnlineStatus($"Could not resume the unfinished match: {exception.Message}");
                OnlineLog.Report(exception);
            }
            finally
            {
                lobby.SetBusy(false);
            }
        }

        private CancellationToken RestartOnlineCancellation()
        {
            CancelOnline();
            _onlineCancellation = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
            return _onlineCancellation.Token;
        }

        private void CancelOnline()
        {
            if (_onlineCancellation == null)
                return;
            _onlineCancellation.Cancel();
            _onlineCancellation.Dispose();
            _onlineCancellation = null;
        }
    }
}
