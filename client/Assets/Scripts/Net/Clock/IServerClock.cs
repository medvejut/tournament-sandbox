using System.Threading;
using Cysharp.Threading.Tasks;

namespace TournamentSandbox.Net
{
    public interface IServerClock
    {
        bool IsSynced { get; }

        // Epoch ms. Throws InvalidOperationException before the first successful sync.
        long ServerNowMs { get; }

        double LastRttMs { get; }

        UniTask SyncAsync(CancellationToken cancellationToken);
    }
}
