using System;
using TournamentSandbox.Net;
using UnityEngine;

namespace TournamentSandbox.Bootstrap
{
    public static class OnlineLog
    {
        public static void Report(Exception exception)
        {
            if (exception is ApiException)
            {
                Debug.LogWarning($"[online] {exception.Message}");
                return;
            }

            Debug.LogException(exception);
        }
    }
}
