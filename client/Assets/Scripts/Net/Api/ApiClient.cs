using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace TournamentSandbox.Net
{
    public sealed class ApiClient : IApiClient
    {
        private readonly ApiClientOptions _options;
        private readonly RetryPolicy _retryPolicy;

        public ApiClient(ApiClientOptions options, RetryPolicy retryPolicy)
        {
            _retryPolicy = retryPolicy;
            _options = options;
        }

        public string AuthToken { get; private set; }
        public bool IsLoggedIn { get; private set; }

        public event Action<ApiRetry> Retrying;

        private async UniTask<TResponse> SendAsync<TResponse>(string method, string path, object body, bool auth, string idempotencyKey, CancellationToken cancellationToken)
        {
            for (var attempt = 1;; attempt++)
            {
                try
                {
                    return await AttemptAsync<TResponse>(method, path, body, auth, idempotencyKey, cancellationToken);
                }
                catch (ApiException error) when (_retryPolicy.ShouldRetry(error, attempt))
                {
                    var delay = _retryPolicy.GetDelay(attempt);
                    Debug.Log($"Retrying {path} after {delay.TotalMilliseconds}ms due to {error.Message} (attempt {attempt})");
                    Retrying?.Invoke(new ApiRetry(path, attempt, delay, error));
                    await UniTask.Delay(delay, cancellationToken: cancellationToken, delayType: DelayType.Realtime);
                }
            }
        }

        private async UniTask<TResponse> AttemptAsync<TResponse>(string method, string path, object body, bool auth, string idempotencyKey, CancellationToken cancellationToken)
        {
            using var request = method switch
            {
                UnityWebRequest.kHttpVerbGET => UnityWebRequest.Get($"{_options.baseUrl}{path}"),
                UnityWebRequest.kHttpVerbPOST => UnityWebRequest.Post($"{_options.baseUrl}{path}", JsonUtility.ToJson(body), "application/json"),
                _ => throw new ArgumentException($"Unsupported HTTP method: {method}", nameof(method))
            };

            if (auth)
            {
                if (AuthToken == null)
                    throw new InvalidOperationException("Cannot call an authenticated route before logging in.");

                request.SetRequestHeader("Authorization", $"Bearer {AuthToken}");
            }

            if (idempotencyKey != null)
            {
                request.SetRequestHeader("Idempotency-Key", idempotencyKey);
            }

            using var attemptCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            using var attemptTimeout = attemptCancellation.CancelAfterSlim(TimeSpan.FromSeconds(_options.timeoutSeconds), DelayType.Realtime);

            try
            {
                await request.SendWebRequest().ToUniTask(cancellationToken: attemptCancellation.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw ApiException.Timeout();
            }
            catch (UnityWebRequestException)
            {
                // UniTask throws for every non-success; the outcome is read from the request below.
            }

            if (request.result == UnityWebRequest.Result.ProtocolError)
            {
                throw ApiException.HttpError((int)request.responseCode, request.downloadHandler.text);
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                throw ApiException.Network(request.error);
            }

            try
            {
                var response = JsonUtility.FromJson<TResponse>(request.downloadHandler.text);
                return response;
            }
            catch (ArgumentException exception)
            {
                throw new ApiException(0, ErrorCodes.BadResponse, exception.Message, exception);
            }
        }

        public async UniTask<LoginResponse> LoginAsync(string deviceId, CancellationToken cancellationToken)
        {
            var response = await Post<LoginResponse>(
                "/v1/login",
                new LoginRequest { deviceId = deviceId },
                auth: false,
                idempotencyKey: null,
                cancellationToken
            );

            AuthToken = response.token;
            IsLoggedIn = true;

            return response;
        }

        public UniTask<TimeResponse> GetTimeAsync(CancellationToken cancellationToken)
        {
            // No retry here: ServerClock measures this request's RTT.
            return AttemptAsync<TimeResponse>(UnityWebRequest.kHttpVerbGET, "/v1/time", body: null, auth: false, idempotencyKey: null, cancellationToken);
        }

        public UniTask<WalletResponse> GetWalletAsync(CancellationToken cancellationToken)
        {
            return Get<WalletResponse>("/v1/wallet",
                auth: true,
                cancellationToken
            );
        }

        public UniTask<MatchView> EnterAsync(string tournamentId, string idempotencyKey, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(idempotencyKey))
                throw new ArgumentException("Idempotency key is required", nameof(idempotencyKey));

            return Post<MatchView>("/v1/matches",
                new EnterRequest { tournamentId = tournamentId },
                auth: true,
                idempotencyKey,
                cancellationToken
            );
        }

        public UniTask<MatchView> SubmitAsync(string matchId, SubmitRequest request, CancellationToken cancellationToken)
        {
            return Post<MatchView>($"/v1/matches/{matchId}/submit",
                request,
                auth: true,
                idempotencyKey: null,
                cancellationToken
            );
        }

        public UniTask<MatchView> GetMatchAsync(string matchId, CancellationToken cancellationToken)
        {
            return Get<MatchView>($"/v1/matches/{matchId}",
                auth: true,
                cancellationToken
            );
        }

        public UniTask<LiveOpsResponse> GetLiveOpsAsync(CancellationToken cancellationToken)
        {
            return Get<LiveOpsResponse>("/v1/liveops",
                auth: true,
                cancellationToken
            );
        }

        private UniTask<TResponse> Get<TResponse>(string path, bool auth, CancellationToken cancellationToken) =>
            SendAsync<TResponse>(UnityWebRequest.kHttpVerbGET, path, body: null, auth, idempotencyKey: null, cancellationToken);

        private UniTask<TResponse> Post<TResponse>(string path, object body, bool auth, string idempotencyKey, CancellationToken cancellationToken) =>
            SendAsync<TResponse>(UnityWebRequest.kHttpVerbPOST, path, body, auth, idempotencyKey, cancellationToken);
    }
}