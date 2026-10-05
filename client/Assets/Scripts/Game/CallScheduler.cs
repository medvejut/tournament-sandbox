using System;
using System.Collections.Generic;
using TournamentSandbox.Core;

namespace TournamentSandbox.Game
{
    public sealed class CallScheduler
    {
        private readonly BingoGame _game;
        private readonly Dictionary<int, int> _callIndexByNumber = new Dictionary<int, int>();

        public CallScheduler(BingoGame game)
        {
            _game = game ?? throw new ArgumentNullException(nameof(game));
            for (var i = 0; i < game.Calls.Length; i++)
            {
                _callIndexByNumber[game.Calls[i]] = i;
            }
        }

        public int CallCount => _game.Calls.Length;

        // -1 before the match starts.
        public int CallIndexAt(int elapsedMs)
        {
            if (elapsedMs < 0)
                return -1;
            return Math.Min(elapsedMs / Rules.CallIntervalMs, _game.Calls.Length - 1);
        }

        // 0 before the first call.
        public int CurrentNumber(int elapsedMs)
        {
            var callIndex = CallIndexAt(elapsedMs);
            return callIndex < 0 ? 0 : _game.Calls[callIndex];
        }

        // -1 for a number this match never calls.
        public int CallTimeOf(int number) =>
            _callIndexByNumber.TryGetValue(number, out var callIndex) ? Rules.CallTimeMs(callIndex) : -1;

        public bool IsCalled(int number, int elapsedMs)
        {
            var callTime = CallTimeOf(number);
            return callTime >= 0 && callTime <= elapsedMs;
        }

        // -1 once every call has happened.
        public int MsUntilNextCall(int elapsedMs)
        {
            var nextIndex = CallIndexAt(elapsedMs) + 1;
            return nextIndex >= _game.Calls.Length ? -1 : Rules.CallTimeMs(nextIndex) - elapsedMs;
        }

        // Newest first, without the current call.
        public void PreviousNumbers(int elapsedMs, List<int> into, int count)
        {
            into.Clear();
            for (var i = CallIndexAt(elapsedMs) - 1; i >= 0 && into.Count < count; i--)
            {
                into.Add(_game.Calls[i]);
            }
        }

        public static char ColumnLetter(int number) =>
            "BINGO"[Math.Clamp((number - 1) / Rules.NumbersPerColumn, 0, Rules.Size - 1)];
    }
}
