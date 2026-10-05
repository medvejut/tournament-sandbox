using System;
using System.Threading.Tasks;
using NUnit.Framework;
using TournamentSandbox.Net;

namespace TournamentSandbox.Tests
{
    public sealed class RetryPolicyTests
    {
        private const double AlmostOne = 0.999999;
        private static readonly TimeSpan Base = TimeSpan.FromMilliseconds(250);
        private static readonly TimeSpan Max = TimeSpan.FromMilliseconds(4000);

        [TestCase(ErrorCodes.Network)]
        [TestCase(ErrorCodes.Timeout)]
        [TestCase(ErrorCodes.BadResponse)]
        public void IsRetryable_NoResponse_IsTrue(string code) =>
            Assert.That(RetryPolicy.IsRetryable(new ApiException(0, code)), Is.True);

        [TestCase(500)]
        [TestCase(502)]
        [TestCase(503)]
        [TestCase(504)]
        public void IsRetryable_ServerError_IsTrue(int status) =>
            Assert.That(RetryPolicy.IsRetryable(new ApiException(status, "x")), Is.True);

        [TestCase(400, ErrorCodes.BadRequest)]
        [TestCase(401, ErrorCodes.Unauthorized)]
        [TestCase(402, ErrorCodes.InsufficientFunds)]
        [TestCase(404, "match-not-found")]
        [TestCase(409, ErrorCodes.AlreadySubmitted)]
        [TestCase(410, ErrorCodes.MatchExpired)]
        public void IsRetryable_ClientError_IsFalse(int status, string code) =>
            Assert.That(RetryPolicy.IsRetryable(new ApiException(status, code)), Is.False);

        [Test]
        public void IsRetryable_Cancellation_IsFalse()
        {
            Assert.That(RetryPolicy.IsRetryable(new OperationCanceledException()), Is.False);
            Assert.That(RetryPolicy.IsRetryable(new TaskCanceledException()), Is.False);
        }

        [Test]
        public void IsRetryable_OtherException_IsFalse()
        {
            Assert.That(RetryPolicy.IsRetryable(new InvalidOperationException()), Is.False);
            Assert.That(RetryPolicy.IsRetryable(new NullReferenceException()), Is.False);
        }

        [Test]
        public void GetDelay_EachAttempt_DoublesTheCeiling()
        {
            var policy = Policy(AlmostOne);
            AssertMs(policy.GetDelay(1), 250);
            AssertMs(policy.GetDelay(2), 500);
            AssertMs(policy.GetDelay(3), 1000);
            AssertMs(policy.GetDelay(4), 2000);
            AssertMs(policy.GetDelay(5), 4000);
        }

        [Test]
        public void GetDelay_PastTheCap_StaysAtMaxDelay()
        {
            var policy = Policy(AlmostOne);
            AssertMs(policy.GetDelay(6), 4000);
            AssertMs(policy.GetDelay(20), 4000);
        }

        [Test]
        public void GetDelay_HugeAttempt_DoesNotOverflow()
        {
            var policy = Policy(AlmostOne);
            foreach (var attempt in new[] { 31, 32, 63, 64, 1000, int.MaxValue })
            {
                var delay = policy.GetDelay(attempt);
                Assert.That(delay, Is.GreaterThanOrEqualTo(TimeSpan.Zero), $"attempt {attempt}");
                Assert.That(delay, Is.LessThanOrEqualTo(Max), $"attempt {attempt}");
            }
        }

        [Test]
        public void GetDelay_IsRandomTimesCeiling()
        {
            AssertMs(Policy(0).GetDelay(3), 0);
            AssertMs(Policy(0.5).GetDelay(3), 500);
            AssertMs(Policy(0.25).GetDelay(10), 1000);
        }

        [Test]
        public void GetDelay_EachCall_DrawsANewRandom()
        {
            var values = new[] { 0.1, 0.9 };
            var calls = 0;
            var policy = new RetryPolicy(5, Base, Max, () => values[calls++ % values.Length]);
            AssertMs(policy.GetDelay(1), 25);
            AssertMs(policy.GetDelay(1), 225);
        }

        [Test]
        public void GetDelay_DefaultRandom_StaysInRange()
        {
            var policy = new RetryPolicy(5, Base, Max);
            for (var i = 0; i < 200; i++)
            {
                var delay = policy.GetDelay(2);
                Assert.That(delay, Is.GreaterThanOrEqualTo(TimeSpan.Zero));
                Assert.That(delay, Is.LessThan(TimeSpan.FromMilliseconds(500)));
            }
        }

        [Test]
        public void GetDelay_AttemptBelowOne_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Policy(0.5).GetDelay(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => Policy(0.5).GetDelay(-1));
        }

        [Test]
        public void ShouldRetry_RetryableError_UntilMaxAttempts()
        {
            var policy = Policy(0.5, maxAttempts: 3);
            var error = new ApiException(503, ErrorCodes.ChaosFail);
            Assert.That(policy.ShouldRetry(error, 1), Is.True);
            Assert.That(policy.ShouldRetry(error, 2), Is.True);
            Assert.That(policy.ShouldRetry(error, 3), Is.False, "3 attempts made = budget spent");
        }

        [Test]
        public void ShouldRetry_NonRetryableError_IsFalse() =>
            Assert.That(Policy(0.5).ShouldRetry(new ApiException(409, ErrorCodes.AlreadySubmitted), 1), Is.False);

        [Test]
        public void ShouldRetry_SingleAttemptPolicy_IsFalse() =>
            Assert.That(Policy(0.5, maxAttempts: 1).ShouldRetry(new ApiException(503, ErrorCodes.ChaosFail), 1), Is.False);

        [Test]
        public void Constructor_ExposesItsSettings()
        {
            var policy = Policy(0.5, maxAttempts: 7);
            Assert.That(policy.MaxAttempts, Is.EqualTo(7));
            Assert.That(policy.BaseDelay, Is.EqualTo(Base));
            Assert.That(policy.MaxDelay, Is.EqualTo(Max));
        }

        [Test]
        public void Constructor_Defaults_AreSane()
        {
            var policy = new RetryPolicy();
            Assert.That(policy.MaxAttempts, Is.GreaterThanOrEqualTo(1));
            Assert.That(policy.BaseDelay, Is.GreaterThan(TimeSpan.Zero));
            Assert.That(policy.MaxDelay, Is.GreaterThanOrEqualTo(policy.BaseDelay));
        }

        [Test]
        public void Constructor_BadSettings_Throw()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new RetryPolicy(0, Base, Max));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RetryPolicy(3, TimeSpan.Zero, Max));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RetryPolicy(3, Max, Base));
        }

        private static RetryPolicy Policy(double random, int maxAttempts = 5) => new RetryPolicy(maxAttempts, Base, Max, () => random);

        private static void AssertMs(TimeSpan actual, double expectedMs) =>
            Assert.That(actual.TotalMilliseconds, Is.EqualTo(expectedMs).Within(0.5));
    }
}
