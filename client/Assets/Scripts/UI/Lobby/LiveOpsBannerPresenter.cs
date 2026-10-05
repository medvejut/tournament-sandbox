using System;
using TournamentSandbox.Game;
using TournamentSandbox.Net;

namespace TournamentSandbox.UI
{
    public sealed class LiveOpsBannerPresenter
    {
        private readonly LobbyView _lobby;
        private readonly LiveOpsService _liveOps;
        private readonly IServerClock _clock;

        private LiveOpsEventDto _shownEvent;
        private int _shownSeconds;

        public LiveOpsBannerPresenter(LobbyView lobby, LiveOpsService liveOps, IServerClock clock)
        {
            _lobby = lobby;
            _liveOps = liveOps;
            _clock = clock;
        }

        public void Tick()
        {
            if (!_lobby.isActiveAndEnabled)
                return;

            var liveOpsEvent = _liveOps.FindShownEvent();
            if (liveOpsEvent == null)
            {
                if (_shownEvent != null)
                {
                    _shownEvent = null;
                    _lobby.ShowLiveOps(null);
                }
                return;
            }

            var nowMs = _clock.ServerNowMs;
            var isActive = liveOpsEvent.IsActive(nowMs);
            var untilMs = (isActive ? liveOpsEvent.endsAt : liveOpsEvent.startsAt) - nowMs;
            var seconds = (int)Math.Ceiling(untilMs / 1000.0);
            if (liveOpsEvent == _shownEvent && seconds == _shownSeconds)
                return;

            _shownEvent = liveOpsEvent;
            _shownSeconds = seconds;
            var countdown = $"{seconds / 60}:{seconds % 60:00}";
            var title = $"DOUBLE REWARDS x{liveOpsEvent.multiplier:0.#}";
            _lobby.ShowLiveOps(isActive ? $"{title}  |  ends in {countdown}" : $"{title}  |  starts in {countdown}");
        }
    }
}
