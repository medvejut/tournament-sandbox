using System;
using TournamentSandbox.Core;

namespace TournamentSandbox.Net
{
    // Server times: realtimeSinceStartup restarts with the process.
    [Serializable]
    public sealed class PendingSubmission
    {
        public string matchId;
        public string tournamentId;
        public long seed;
        public long startedAtServerMs;
        public long deadlineServerMs;
        public Move[] moves;
        public int clientScore;

        public uint Seed => (uint)seed;

        public bool IsValid => !string.IsNullOrEmpty(matchId) && moves != null;
    }
}
