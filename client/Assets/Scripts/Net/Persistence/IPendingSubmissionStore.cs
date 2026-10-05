namespace TournamentSandbox.Net
{
    public interface IPendingSubmissionStore
    {
        void Save(PendingSubmission submission);

        // An unreadable file counts as nothing pending, and is deleted.
        bool TryLoad(out PendingSubmission submission);

        void Clear();
    }
}
