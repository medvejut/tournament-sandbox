namespace TournamentSandbox.Core
{
    public readonly struct ReplayResult
    {
        public bool Ok { get; }
        public int Score { get; }
        public int Daubs { get; }
        public int Lines { get; }
        public int FalseBingos { get; }
        public string Reason { get; } // null when Ok
        public int MoveIndex { get; } // -1 when Ok

        private ReplayResult(bool ok, int score, int daubs, int lines, int falseBingos, string reason, int moveIndex)
        {
            Ok = ok;
            Score = score;
            Daubs = daubs;
            Lines = lines;
            FalseBingos = falseBingos;
            Reason = reason;
            MoveIndex = moveIndex;
        }

        public static ReplayResult Success(int score, int daubs, int lines, int falseBingos) =>
            new ReplayResult(true, score, daubs, lines, falseBingos, null, -1);

        public static ReplayResult Reject(string reason, int moveIndex) =>
            new ReplayResult(false, 0, 0, 0, 0, reason, moveIndex);

        public override string ToString() =>
            Ok ? $"ok score={Score} daubs={Daubs} lines={Lines} falseBingos={FalseBingos}" : $"reject {Reason} at move {MoveIndex}";
    }
}
