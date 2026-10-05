using System;
using UnityEngine;

namespace TournamentSandbox.Net
{
    // Status 0 means there was no HTTP response.
    public sealed class ApiException : Exception
    {
        public int Status { get; }
        public string Code { get; }

        public ApiException(int status, string code, string message = null, Exception inner = null)
            : base(message ?? $"{(status == 0 ? "no response" : $"HTTP {status}")}: {code}", inner)
        {
            Status = status;
            Code = code;
        }

        public bool IsNoResponse => Status == 0;
        public bool IsServerError => Status >= 500;
        public bool IsClientError => Status >= 400 && Status < 500;

        public static ApiException Network(string message, Exception inner = null) =>
            new ApiException(0, ErrorCodes.Network, message, inner);

        public static ApiException Timeout(string message = null) =>
            new ApiException(0, ErrorCodes.Timeout, message ?? "request timed out");

        public static ApiException HttpError(int status, string body)
        {
            ErrorResponse response = null;
            try
            {
                response = JsonUtility.FromJson<ErrorResponse>(body);
            }
            catch (ArgumentException)
            {
                // Not JSON, e.g. a proxy error page.
            }

            var error = response?.error;
            return string.IsNullOrEmpty(error?.code)
                ? new ApiException(status, $"http-{status}")
                : new ApiException(status, error.code, error.message);
        }
    }
}