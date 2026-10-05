using System;
using System.Collections.Generic;
using TournamentSandbox.Core;
using TournamentSandbox.Game;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace TournamentSandbox.UI
{
    public sealed class MatchPresenter
    {
        private readonly MatchCardView _card;
        private readonly MatchCallsView _calls;
        private readonly MatchHudView _hud;
        private readonly int _goodSpeedBonus;

        private double _startRealtime;
        private bool _isRunning;

        public MatchRecorder Recorder { get; private set; }

        public event Action<MatchRecorder> MovesChanged;

        // Raised once, when the time is up or Finish is called.
        public event Action<MatchRecorder> Finished;

        public MatchPresenter(MatchCardView card, MatchCallsView calls, MatchHudView hud, Button bingoButton, int goodSpeedBonus)
        {
            _card = card;
            _calls = calls;
            _hud = hud;
            _goodSpeedBonus = goodSpeedBonus;
            card.CellTapped += OnCellTapped;
            bingoButton.onClick.AddListener(OnBingo);
        }

        // Monotonic, not summed frame deltas: hitches and backgrounding can't make it drift.
        private int ElapsedMs => (int)Math.Floor((Time.realtimeSinceStartupAsDouble - _startRealtime) * 1000.0);

        // Views fetch their components in Awake: call once the screen is active. elapsedMs < 0 is a countdown.
        public void Start(BingoGame game, IReadOnlyList<Move> savedMoves, int elapsedMs)
        {
            Recorder = new MatchRecorder(game, savedMoves);
            Recorder.Changed += recorder => MovesChanged?.Invoke(recorder);
            _card.Bind(game);
            ShowMarks();
            _calls.ResetView();
            _hud.ResetView();
            _startRealtime = Time.realtimeSinceStartupAsDouble - elapsedMs / 1000.0;
            _isRunning = true;
        }

        public void Tick()
        {
            if (!_isRunning)
                return;

            var elapsedMs = ElapsedMs;
            _calls.Render(Recorder.Calls, elapsedMs);
            _hud.Render(elapsedMs, Recorder.Prediction.Score);

            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.spaceKey.wasPressedThisFrame)
            {
                OnBingo();
            }

            if (elapsedMs >= Rules.MatchDurationMs)
            {
                Finish();
            }
        }

        public void Finish()
        {
            if (!_isRunning)
                return;
            _isRunning = false;
            Finished?.Invoke(Recorder);
        }

        public void Abandon() => _isRunning = false;

        private void OnCellTapped(int cell)
        {
            if (!_isRunning)
                return;

            var elapsedMs = ElapsedMs;
            switch (Recorder.TryDaub(cell, elapsedMs))
            {
                case TapResult.Accepted:
                    _card.ShowDaubed(cell);
                    ToastDaub(elapsedMs - Recorder.Calls.CallTimeOf(Recorder.Game.Card[cell]));
                    break;
                case TapResult.NotCalledYet:
                    _card.Flash(cell);
                    break;
                case TapResult.TooSoonAfterCall:
                case TapResult.TooFastAfterLastMove:
                    _hud.Toast("too quick", ToastTone.Dim, 0.5f);
                    break;
                case TapResult.MoveLimitReached:
                    _hud.Toast("move limit reached", ToastTone.Bad);
                    break;
            }
        }

        private void ToastDaub(int reactionMs)
        {
            var bonus = Rules.SpeedBonus(reactionMs);
            var message = bonus > 0 ? $"+{Rules.DaubPoints + bonus}  ({reactionMs / 1000f:0.0}s)" : $"+{Rules.DaubPoints}";
            _hud.Toast(message, bonus >= _goodSpeedBonus ? ToastTone.Good : ToastTone.Neutral, 0.8f);
        }

        private void OnBingo()
        {
            if (!_isRunning)
                return;

            switch (Recorder.TryClaimBingo(ElapsedMs, out var newLines))
            {
                case TapResult.Accepted:
                    break;
                case TapResult.OutOfTime:
                    _hud.Toast("wait for the first ball", ToastTone.Dim, 0.8f);
                    return;
                case TapResult.MoveLimitReached:
                    _hud.Toast("move limit reached", ToastTone.Bad);
                    return;
                default:
                    _hud.Toast("too quick", ToastTone.Dim, 0.5f);
                    return;
            }

            if (newLines == 0)
            {
                _hud.Toast($"FALSE BINGO  -{Rules.FalseBingoPenalty}", ToastTone.Bad, 1.4f);
                return;
            }

            ShowClaimedLines();
            var points = Rules.LinePoints * newLines;
            _hud.Toast(newLines == 1 ? $"BINGO!  +{points}" : $"BINGO x{newLines}!  +{points}", ToastTone.Highlight, 1.6f);
        }

        private void ShowMarks()
        {
            for (var cell = 0; cell < Rules.CellCount; cell++)
            {
                if (cell != Rules.FreeCell && Recorder.IsDaubed(cell))
                {
                    _card.ShowDaubed(cell);
                }
            }
            ShowClaimedLines();
        }

        private void ShowClaimedLines()
        {
            for (var lineIndex = 0; lineIndex < Bingo.Lines.Count; lineIndex++)
            {
                if (!Recorder.IsLineClaimed(lineIndex))
                    continue;
                foreach (var cell in Bingo.Lines[lineIndex])
                {
                    _card.ShowClaimed(cell);
                }
            }
        }
    }
}
