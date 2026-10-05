using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using TournamentSandbox.Net;

namespace TournamentSandbox.Game
{
    public sealed class LiveOpsService
    {
        private readonly IApiClient _api;
        private readonly IServerClock _clock;

        private LiveOpsEventDto[] _events = Array.Empty<LiveOpsEventDto>();

        public LiveOpsService(IApiClient api, IServerClock clock)
        {
            _api = api ?? throw new ArgumentNullException(nameof(api));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        public async UniTask RefreshAsync(CancellationToken cancellationToken)
        {
            var response = await _api.GetLiveOpsAsync(cancellationToken);
            _events = response.events ?? Array.Empty<LiveOpsEventDto>();
        }

        // The running event, else the next one to start.
        public LiveOpsEventDto FindShownEvent()
        {
            if (!_clock.IsSynced)
                return null;

            var nowMs = _clock.ServerNowMs;
            LiveOpsEventDto next = null;
            foreach (var liveOpsEvent in _events)
            {
                if (liveOpsEvent.IsActive(nowMs))
                    return liveOpsEvent;
                if (liveOpsEvent.startsAt > nowMs && (next == null || liveOpsEvent.startsAt < next.startsAt))
                {
                    next = liveOpsEvent;
                }
            }
            return next;
        }
    }
}
