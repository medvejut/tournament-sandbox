using System.Collections.Generic;
using TournamentSandbox.Core;
using TournamentSandbox.Game;
using TMPro;
using UnityEngine;

namespace TournamentSandbox.UI
{
    public sealed class MatchCallsView : MonoBehaviour
    {
        private const string FirstCallCaption = "First call in...";
        private const string PreviousCallsCaption = "Previous calls";

        [SerializeField] private MatchCallBallView currentBall;

        [Tooltip("Newest first.")]
        [SerializeField] private MatchCallBallView[] previousBalls;

        [SerializeField] private TMP_Text caption;

        [Tooltip("Its anchorMax.x is driven 1 -> 0 as the next call approaches.")]
        [SerializeField] private RectTransform nextCallFill;

        [Header("New ball pop")]
        [Min(0f)]
        [SerializeField] private float popScale = 0.15f;
        [Tooltip("Pop strength lost per second; 4 settles in a quarter second.")]
        [Min(0.01f)]
        [SerializeField] private float popDecayPerSecond = 4f;

        private readonly List<int> _previousNumbers = new List<int>();
        private int _shownNumber = -1;
        private float _pop;

        private void Update()
        {
            if (_pop <= 0f)
                return;
            _pop = Mathf.Max(0f, _pop - Time.unscaledDeltaTime * popDecayPerSecond);
            currentBall.transform.localScale = Vector3.one * (1f + popScale * _pop);
        }

        // A negative elapsedMs is the pre-match countdown.
        public void Render(CallScheduler calls, int elapsedMs)
        {
            var number = calls.CurrentNumber(elapsedMs);
            if (number != _shownNumber)
            {
                _shownNumber = number;
                currentBall.Show(number);
                caption.text = number == 0 ? FirstCallCaption : PreviousCallsCaption;
                _pop = number == 0 ? 0f : 1f;

                calls.PreviousNumbers(elapsedMs, _previousNumbers, previousBalls.Length);
                for (var i = 0; i < previousBalls.Length; i++)
                {
                    previousBalls[i].Show(i < _previousNumbers.Count ? _previousNumbers[i] : 0);
                }
            }

            var msUntilNext = elapsedMs < 0 ? -elapsedMs : calls.MsUntilNextCall(elapsedMs);
            var progress = msUntilNext < 0 ? 0f : Mathf.Clamp01(msUntilNext / (float)Rules.CallIntervalMs);
            nextCallFill.anchorMax = new Vector2(progress, 1f);
        }

        public void ResetView()
        {
            _shownNumber = -1;
            _pop = 0f;
            currentBall.transform.localScale = Vector3.one;
        }
    }
}
