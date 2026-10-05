using System;

namespace TournamentSandbox.Net
{
    [Serializable]
    public sealed class ApiClientOptions
    {
        public string baseUrl = "http://localhost:8080";
        public float timeoutSeconds = 10f; // per attempt, not across retries
    }
}
