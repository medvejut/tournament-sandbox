namespace TournamentSandbox.Core
{
    public static class RejectReason
    {
        public const string MalformedMove = "malformed-move";
        public const string TooManyMoves = "too-many-moves";
        public const string OutsideMatchTime = "outside-match-time";
        public const string ImplausibleSpeed = "implausible-speed";
        public const string CellOutOfRange = "cell-out-of-range";
        public const string CellAlreadyDaubed = "cell-already-daubed";
        public const string NumberNeverCalled = "number-never-called";
        public const string DaubBeforeCall = "daub-before-call";
        public const string ImplausibleReaction = "implausible-reaction";
    }
}
