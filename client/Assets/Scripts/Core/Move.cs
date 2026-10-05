// Wire format of a move. Flat because JsonUtility has no unions.

using System;

namespace TournamentSandbox.Core
{
    [Serializable]
    public struct Move
    {
        public const string Daub = "daub";
        public const string Bingo = "bingo";

        public string kind;
        public int cell; // -1 for a bingo claim
        public int t;

        public static Move DaubAt(int cell, int t) => new Move { kind = Daub, cell = cell, t = t };

        public static Move BingoAt(int t) => new Move { kind = Bingo, cell = -1, t = t };

        public override string ToString() => $"{kind}({cell})@{t}";
    }
}
