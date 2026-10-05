namespace TournamentSandbox.Core
{
    public sealed class BingoGame
    {
        public int[] Card { get; } // 0 = the free centre
        public int[] Calls { get; } // Calls[i] is called at Rules.CallTimeMs(i)

        public BingoGame(int[] card, int[] calls)
        {
            Card = card;
            Calls = calls;
        }
    }
}
