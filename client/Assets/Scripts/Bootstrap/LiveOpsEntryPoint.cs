using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using TournamentSandbox.Game;
using TournamentSandbox.Net;
using TournamentSandbox.UI;
using UnityEngine;
using VContainer.Unity;

namespace TournamentSandbox.Bootstrap
{
    public sealed class LiveOpsEntryPoint : IAsyncStartable, ITickable, IDisposable
    {
        private readonly OnlineMatchService _online;
        private readonly LiveOpsService _liveOps;
        private readonly IServerClock _clock;
        private readonly LiveOpsBannerPresenter _banner;
        private readonly TimeSpan _pollInterval;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();

        public LiveOpsEntryPoint(OnlineMatchService online, LiveOpsService liveOps, IServerClock clock, LiveOpsBannerPresenter banner, TimeSpan pollInterval)
        {
            _online = online;
            _liveOps = liveOps;
            _clock = clock;
            _banner = banner;
            _pollInterval = pollInterval;
            Application.focusChanged += OnFocusChanged;
        }

        // VContainer logs whatever StartAsync throws as an error, cancellation included, so it ends quietly.
        public async UniTask StartAsync(CancellationToken cancellation)
        {
            while (true)
            {
                try
                {
                    await _online.EnsureClockSyncedAsync(cancellation);
                    await _online.EnsureLoggedInAsync(cancellation);
                    await _liveOps.RefreshAsync(cancellation);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception exception)
                {
                    OnlineLog.Report(exception);
                }

                if (await UniTask.Delay(_pollInterval, DelayType.Realtime, cancellationToken: cancellation).SuppressCancellationThrow())
                    return;
            }
        }

        public void Tick() => _banner.Tick();

        public void Dispose()
        {
            Application.focusChanged -= OnFocusChanged;
            _lifetime.Cancel();
            _lifetime.Dispose();
        }

        // Entry points get no OnApplicationPause, but focus returns after every resume from background.
        private void OnFocusChanged(bool hasFocus)
        {
            if (hasFocus && _clock.IsSynced)
            {
                ResyncClockAsync(_lifetime.Token).Forget();
            }
        }

        private async UniTask ResyncClockAsync(CancellationToken cancellationToken)
        {
            try
            {
                await _clock.SyncAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                OnlineLog.Report(exception);
            }
        }
    }
}
