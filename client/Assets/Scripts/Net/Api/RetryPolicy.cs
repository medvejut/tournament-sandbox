using System;

namespace TournamentSandbox.Net
{
    public sealed class RetryPolicy
    {
        private static readonly TimeSpan BaseFallback = TimeSpan.FromMilliseconds(250);
        private static readonly TimeSpan MaxFallback = TimeSpan.FromMilliseconds(4000);

        private readonly Func<double> _random;

        public int MaxAttempts { get; }
        public TimeSpan BaseDelay { get; }
        public TimeSpan MaxDelay { get; }

        public RetryPolicy(int maxAttempts = 5, TimeSpan? baseDelay = null, TimeSpan? maxDelay = null, Func<double> random = null)
        {
            if (maxAttempts < 1)
                throw new ArgumentOutOfRangeException(nameof(maxAttempts), "must be >= 1");
            MaxAttempts = maxAttempts;

            BaseDelay = baseDelay ?? BaseFallback;
            if (BaseDelay <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(baseDelay), "must be > 0");

            MaxDelay = maxDelay ?? MaxFallback;
            if (MaxDelay < BaseDelay)
                throw new ArgumentOutOfRangeException(nameof(maxDelay), "must be >= baseDelay");

            _random = random ?? new Random().NextDouble;
        }

        public TimeSpan GetDelay(int attempt)
        {
            if (attempt < 1)
                throw new ArgumentOutOfRangeException(nameof(attempt), "must be >= 1");

            var delay = _random.Invoke() * Math.Min(MaxDelay.TotalMilliseconds, BaseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1));
            return TimeSpan.FromMilliseconds(delay);
        }

        public bool ShouldRetry(Exception error, int attempt)
        {
            return attempt < MaxAttempts && IsRetryable(error);
        }

        public static bool IsRetryable(Exception error)
        {
            return error switch
            {
                OperationCanceledException or ApiException { IsClientError: true } => false,
                ApiException { IsNoResponse: true } or ApiException { IsServerError: true } => true,
                _ => false
            };
        }
    }
}