using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace TournamentSandbox.Net
{
    public interface IApiClient
    {
        string AuthToken { get; }
        bool IsLoggedIn { get; }

        event Action<ApiRetry> Retrying;

        UniTask<LoginResponse> LoginAsync(string deviceId, CancellationToken cancellationToken);
        UniTask<TimeResponse> GetTimeAsync(CancellationToken cancellationToken);
        UniTask<WalletResponse> GetWalletAsync(CancellationToken cancellationToken);

        UniTask<MatchView> EnterAsync(string tournamentId, string idempotencyKey, CancellationToken cancellationToken);

        UniTask<MatchView> SubmitAsync(string matchId, SubmitRequest request, CancellationToken cancellationToken);
        UniTask<MatchView> GetMatchAsync(string matchId, CancellationToken cancellationToken);
        UniTask<LiveOpsResponse> GetLiveOpsAsync(CancellationToken cancellationToken);
    }
}
