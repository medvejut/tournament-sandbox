// JsonUtility DTOs of the server API. The server sends null for values not known yet, which
// JsonUtility reads as 0 or null, so check status before trusting them.

using System;
using TournamentSandbox.Core;

namespace TournamentSandbox.Net
{
    public static class MatchStatus
    {
        public const string Playing = "playing";
        public const string WaitingOpponent = "waiting-opponent";
        public const string Completed = "completed";
        public const string Refunded = "refunded";
    }

    public static class MatchResult
    {
        public const string Win = "win";
        public const string Loss = "loss";
        public const string Tie = "tie";
    }

    public static class ErrorCodes
    {
        // Status 0: no HTTP response.
        public const string Network = "network-error";
        public const string Timeout = "timeout";
        public const string BadResponse = "bad-response";

        public const string BadRequest = "bad-request";
        public const string IdempotencyKeyRequired = "idempotency-key-required";
        public const string Unauthorized = "unauthorized";
        public const string InsufficientFunds = "insufficient-funds";
        public const string AlreadySubmitted = "already-submitted";
        public const string MatchExpired = "match-expired";
        public const string ChaosFail = "chaos-fail";
    }

    [Serializable]
    public sealed class LoginRequest
    {
        public string deviceId;
    }

    [Serializable]
    public sealed class LoginResponse
    {
        public string playerId;
        public string token;
        public int balance;
        public long serverTime;
    }

    [Serializable]
    public sealed class TimeResponse
    {
        public long serverTime;
    }

    [Serializable]
    public sealed class WalletResponse
    {
        public int balance;
    }

    [Serializable]
    public sealed class EnterRequest
    {
        public string tournamentId;
    }

    [Serializable]
    public sealed class SubmitRequest
    {
        public Move[] moves;
        public int clientScore;
    }

    [Serializable]
    public sealed class MatchView
    {
        public string matchId;
        public string tournamentId;
        public long seed;
        public int durationMs;
        public long startedAt;
        public long deadline;
        public float multiplier;
        public string status;
        public int score; // meaningful once status != playing
        public int opponentScore; // meaningful only when completed
        public string result;
        public int payout;
        public int balance;
        public string rejectedReason;
        public string[] flags;

        public uint Seed => (uint)seed;
        public bool IsFinal => status == MatchStatus.Completed || status == MatchStatus.Refunded;
        public bool HasFlag(string flag) => flags != null && Array.IndexOf(flags, flag) >= 0;
    }

    [Serializable]
    public sealed class LiveOpsEventDto
    {
        public string id;
        public string type;
        public long startsAt;
        public long endsAt;
        public float multiplier;

        public bool IsActive(long serverNowMs) => startsAt <= serverNowMs && serverNowMs < endsAt;
    }

    [Serializable]
    public sealed class LiveOpsResponse
    {
        public long serverTime;
        public LiveOpsEventDto[] events;
    }

    [Serializable]
    public sealed class ErrorBody
    {
        public string code;
        public string message;
    }

    [Serializable]
    public sealed class ErrorResponse
    {
        public ErrorBody error;
    }
}
