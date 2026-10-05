using System;
using TournamentSandbox.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TournamentSandbox.UI
{
    public sealed class MatchHudView : MonoBehaviour
    {
        private const int NoCountdown = -1;

        [SerializeField] private TMP_Text timerLabel;
        [SerializeField] private TMP_Text scoreLabel;
        [SerializeField] private TMP_Text countdownLabel;
        [SerializeField] private TMP_Text toastLabel;
        [SerializeField] private Button quitButton;

        [Header("Timer")]
        [SerializeField] private Color timerColor = Color.white;
        [SerializeField] private Color timerWarningColor = Color.red;
        [Tooltip("Seconds left from which the timer turns to the warning colour.")]
        [Min(0)]
        [SerializeField] private int warningSeconds = 10;

        [Header("Countdown")]
        [Tooltip("How long \"GO!\" stays after the match starts.")]
        [Min(0)]
        [SerializeField] private int goShownMs = 600;

        [Header("Toast")]
        [Min(0.01f)]
        [SerializeField] private float toastFadeSeconds = 0.4f;
        [SerializeField] private Color neutralToastColor = Color.white;
        [SerializeField] private Color goodToastColor = Color.green;
        [SerializeField] private Color badToastColor = Color.red;
        [SerializeField] private Color dimToastColor = Color.gray;
        [SerializeField] private Color highlightToastColor = Color.yellow;

        private float _toastLeft;
        private int _shownSeconds;
        private int _shownScore;
        private int _shownCountdown;

        public event Action QuitClicked;

        private void Awake()
        {
            quitButton.onClick.AddListener(() => QuitClicked?.Invoke());
            ResetView();
        }

        private void Update()
        {
            if (_toastLeft <= 0f)
                return;
            _toastLeft -= Time.unscaledDeltaTime;
            toastLabel.alpha = Mathf.Clamp01(_toastLeft / toastFadeSeconds);
            if (_toastLeft <= 0f)
            {
                toastLabel.text = "";
            }
        }

        // Assigning text rebuilds the mesh, so only on change.
        public void Render(int elapsedMs, int score)
        {
            var remainingMs = Mathf.Clamp(Rules.MatchDurationMs - Mathf.Max(0, elapsedMs), 0, Rules.MatchDurationMs);
            var seconds = Mathf.CeilToInt(remainingMs / 1000f);
            if (seconds != _shownSeconds)
            {
                _shownSeconds = seconds;
                timerLabel.text = $"{seconds / 60}:{seconds % 60:00}";
                timerLabel.color = seconds <= warningSeconds ? timerWarningColor : timerColor;
            }

            if (score != _shownScore)
            {
                _shownScore = score;
                scoreLabel.text = score.ToString();
            }

            var countdown = CountdownAt(elapsedMs);
            if (countdown != _shownCountdown)
            {
                _shownCountdown = countdown;
                countdownLabel.text = countdown > 0 ? countdown.ToString() : countdown == 0 ? "GO!" : "";
            }
        }

        public void Toast(string message, ToastTone tone, float seconds = 1.1f)
        {
            toastLabel.text = message;
            toastLabel.color = ColorOf(tone);
            toastLabel.alpha = 1f;
            _toastLeft = seconds;
        }

        public void ResetView()
        {
            _shownSeconds = _shownScore = _shownCountdown = int.MinValue;
            toastLabel.text = "";
            _toastLeft = 0f;
        }

        // Seconds left before the start, 0 while "GO!" shows, NoCountdown after that.
        private int CountdownAt(int elapsedMs)
        {
            if (elapsedMs < 0)
                return Mathf.CeilToInt(-elapsedMs / 1000f);
            return elapsedMs < goShownMs ? 0 : NoCountdown;
        }

        private Color ColorOf(ToastTone tone)
        {
            switch (tone)
            {
                case ToastTone.Good:
                    return goodToastColor;
                case ToastTone.Bad:
                    return badToastColor;
                case ToastTone.Dim:
                    return dimToastColor;
                case ToastTone.Highlight:
                    return highlightToastColor;
                default:
                    return neutralToastColor;
            }
        }
    }
}
