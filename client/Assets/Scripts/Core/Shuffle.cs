using System.Collections.Generic;

namespace TournamentSandbox.Core
{
    public static class Shuffle
    {
    // Same order of rng use as the server's shuffle.
        public static void InPlace<T>(IList<T> list, XorShift32 random)
        {
            for (var i = list.Count - 1; i > 0; i--)
            {
                var j = random.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
