using System;

namespace TournamentSandbox.Net
{
    public sealed class ApiRetry
    {
        public string Path { get; }
        public int FailedAttempt { get; } // 1-based
        public TimeSpan Delay { get; }
        public ApiException Error { get; }

        public ApiRetry(string path, int failedAttempt, TimeSpan delay, ApiException error)
        {
            Path = path;
            FailedAttempt = failedAttempt;
            Delay = delay;
            Error = error;
        }
    }
}
