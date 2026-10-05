// Port of server/src/prng.ts. uint arithmetic wraps like `>>> 0` in TS.

using System;

namespace TournamentSandbox.Core
{
    public sealed class XorShift32
    {
        private const ulong Range = 1UL << 32;
        private const uint ZeroSeedReplacement = 0x9E3779B9u;

        private uint _state;

        public XorShift32(uint seed)
        {
            _state = seed != 0 ? seed : ZeroSeedReplacement;
        }

        public uint NextUInt()
        {
            var state = _state;
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            _state = state;
            return state;
        }

        // Rejection sampling: a plain modulo would be biased.
        public uint Next(ulong maxExclusive)
        {
            if (maxExclusive == 0 || maxExclusive > Range)
                throw new ArgumentOutOfRangeException(nameof(maxExclusive), maxExclusive, "must be in 1..2^32");

            var limit = Range - Range % maxExclusive;
            uint value;
            do
            {
                value = NextUInt();
            } while (value >= limit);
            return (uint)(value % maxExclusive);
        }

        public int Next(int maxExclusive)
        {
            if (maxExclusive <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxExclusive), maxExclusive, "must be > 0");
            return (int)Next((ulong)maxExclusive);
        }
    }
}
