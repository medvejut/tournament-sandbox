// Must match RULES in server/src/game.ts.

using System;

namespace TournamentSandbox.Core
{
    public static class Rules
    {
        public const int Size = 5;
        public const int CellCount = Size * Size;
        public const int FreeCell = 12;
        public const int NumbersPerColumn = 15; // B 1-15, I 16-30, N 31-45, G 46-60, O 61-75
        public const int CallIntervalMs = 2000;
        public const int MatchDurationMs = 120_000;
        public const int CallCount = (MatchDurationMs + CallIntervalMs - 1) / CallIntervalMs;
        public const int DaubPoints = 100;
        public const int MaxSpeedBonus = 50;
        public const int SpeedBonusDecayMs = 100;
        public const int MinReactionMs = 200;
        public const int MinMoveIntervalMs = 150;
        public const int LinePoints = 500;
        public const int FalseBingoPenalty = 200;
        public const int MaxMoves = 64;

        public static int CallTimeMs(int callIndex) => callIndex * CallIntervalMs;

        public static int SpeedBonus(int reactionMs) => Math.Max(0, MaxSpeedBonus - reactionMs / SpeedBonusDecayMs);
    }
}
