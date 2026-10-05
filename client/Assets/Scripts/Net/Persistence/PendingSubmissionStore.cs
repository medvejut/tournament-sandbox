using System;
using System.IO;
using UnityEngine;

namespace TournamentSandbox.Net
{
    public sealed class PendingSubmissionStore : IPendingSubmissionStore
    {
        public const string FileName = "pending_submission.json";
        public const string TempSuffix = ".tmp";

        private readonly string _directory;
        private readonly Action<string> _logWarning;
        public string FilePath { get; }
        public string TempPath => FilePath + TempSuffix;

        public PendingSubmissionStore(string directory, Action<string> logWarning = null)
        {
            _logWarning = logWarning ?? Debug.LogWarning;
            _directory = directory;
            FilePath = Path.Combine(directory, FileName);
        }

        public void Save(PendingSubmission submission)
        {
            if (submission is not { IsValid: true })
                throw new ArgumentException($"Submission is null or invalid: {submission}", nameof(submission));

            Directory.CreateDirectory(_directory);
            var json = JsonUtility.ToJson(submission);
            File.WriteAllText(TempPath, json);

            if (File.Exists(FilePath))
            {
                File.Replace(TempPath, FilePath, null);
            }
            else
            {
                File.Move(TempPath, FilePath);
            }
        }

        public bool TryLoad(out PendingSubmission submission)
        {
            submission = null;
            if (!File.Exists(FilePath))
            {
                return false;
            }

            string error;
            try
            {
                var json = File.ReadAllText(FilePath);
                var loaded = JsonUtility.FromJson<PendingSubmission>(json);
                if (loaded is { IsValid: true })
                {
                    submission = loaded;
                    return true;
                }

                error = "Submission is null or invalid";
            }
            catch (Exception exception)
            {
                error = exception.Message;
            }

            _logWarning($"Failed to load pending submission from {FilePath}: {error}. The file will be deleted.");

            try
            {
                File.Delete(FilePath);
            }
            catch (Exception)
            {
                // Retried on the next launch.
            }

            return false;
        }

        public void Clear()
        {
            if (File.Exists(FilePath))
            {
                File.Delete(FilePath);
            }

            if (File.Exists(TempPath))
            {
                File.Delete(TempPath);
            }
        }
    }
}