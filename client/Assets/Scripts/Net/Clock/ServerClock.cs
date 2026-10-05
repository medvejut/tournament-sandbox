using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace TournamentSandbox.Net
{
    public sealed class ServerClock : IServerClock
    {
        private readonly IApiClient _api;
        private readonly Func<double> _realtimeSeconds;
        private double _offsetMs;

        public ServerClock(IApiClient api, Func<double> realtimeSeconds = null)
        {
            _api = api;
            _realtimeSeconds = realtimeSeconds ?? (() => Time.realtimeSinceStartupAsDouble);
        }

        public bool IsSynced { get; private set; }

        public double LastRttMs { get; private set; }

        public long ServerNowMs
        {
            get
            {
                if (!IsSynced)
                    throw new InvalidOperationException("ServerClock.ServerNowMs called before the clock was synced.");

                return (long)Math.Round(_realtimeSeconds() * 1000 + _offsetMs);
            }
        }

        public async UniTask SyncAsync(CancellationToken cancellationToken)
        {
            var t0 = _realtimeSeconds();
            var response = await _api.GetTimeAsync(cancellationToken);
            var t1 = _realtimeSeconds();
            _offsetMs = response.serverTime - (t0 + t1) / 2 * 1000;
            LastRttMs = (t1 - t0) * 1000;
            IsSynced = true;
        }
    }
}