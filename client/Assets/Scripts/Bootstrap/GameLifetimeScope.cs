using System;
using TournamentSandbox.Game;
using TournamentSandbox.Net;
using TournamentSandbox.UI;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace TournamentSandbox.Bootstrap
{
    public sealed class GameLifetimeScope : LifetimeScope
    {
        [SerializeField] private ApiClientOptions server = new ApiClientOptions();
        [Tooltip("Seconds between fetches of the live-ops events. The banner itself follows the server clock every frame.")]
        [Min(1)]
        [SerializeField] private int liveOpsPollSeconds = 15;

        protected override void Configure(IContainerBuilder builder)
        {
            builder.RegisterInstance(server);
            builder.Register(_ => new RetryPolicy(), Lifetime.Singleton);
            builder.Register<IApiClient, ApiClient>(Lifetime.Singleton);
            builder.Register<IServerClock>(resolver => new ServerClock(resolver.Resolve<IApiClient>()), Lifetime.Singleton);
            builder.Register<IPendingSubmissionStore>(_ => new PendingSubmissionStore(Application.persistentDataPath), Lifetime.Singleton);
            builder.Register(resolver => new OnlineMatchService(
                    resolver.Resolve<IApiClient>(),
                    resolver.Resolve<IServerClock>(),
                    resolver.Resolve<IPendingSubmissionStore>(),
                    resolver.Resolve<RetryPolicy>(),
                    SystemInfo.deviceUniqueIdentifier),
                Lifetime.Singleton);
            builder.Register<LiveOpsService>(Lifetime.Singleton);

            builder.RegisterComponentInHierarchy<LobbyView>();
            builder.Register<LiveOpsBannerPresenter>(Lifetime.Singleton);
            builder.RegisterEntryPoint<LiveOpsEntryPoint>().WithParameter(TimeSpan.FromSeconds(liveOpsPollSeconds));
            builder.RegisterComponentInHierarchy<MatchFlow>();
        }
    }
}
