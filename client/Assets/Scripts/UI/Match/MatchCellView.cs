using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TournamentSandbox.UI
{
    [RequireComponent(typeof(Image), typeof(Button))]
    public sealed class MatchCellView : MonoBehaviour
    {
        [SerializeField] private TMP_Text label;

        [Header("Colours")]
        [SerializeField] private Color idleColor = Color.white;
        [SerializeField] private Color idleTextColor = Color.black;
        [SerializeField] private Color daubedColor = Color.red;
        [SerializeField] private Color daubedTextColor = Color.white;
        [SerializeField] private Color claimedColor = Color.yellow;
        [SerializeField] private Color claimedTextColor = Color.black;
        [Tooltip("Pulsed when a tap is refused because the number isn't called yet.")]
        [SerializeField] private Color rejectColor = Color.red;

        [Header("Text")]
        [SerializeField] private float numberFontSize = 76f;
        [SerializeField] private float freeFontSize = 48f;

        [Header("Reject flash")]
        [Min(0.01f)]
        [SerializeField] private float flashSeconds = 0.35f;
        [SerializeField] private float flashWobbleDegrees = 10f;

        private Image _background;
        private Color _baseColor;
        private float _flashLeft;

        public event Action Tapped;

        private void Awake()
        {
            _background = GetComponent<Image>();
            GetComponent<Button>().onClick.AddListener(() => Tapped?.Invoke());
            enabled = false;
        }

        // Runs only while a flash is playing; Flash() enables it.
        private void Update()
        {
            _flashLeft = Mathf.Max(0f, _flashLeft - Time.unscaledDeltaTime);
            var strength = _flashLeft / flashSeconds;
            _background.color = Color.Lerp(_baseColor, rejectColor, strength);
            var wobble = Mathf.Sin((1f - strength) * Mathf.PI * 6f) * flashWobbleDegrees * strength;
            transform.localRotation = Quaternion.Euler(0f, 0f, wobble);
            if (_flashLeft <= 0f)
            {
                enabled = false;
            }
        }

        public void ShowNumber(int number)
        {
            label.text = number.ToString();
            label.fontSize = numberFontSize;
            SetColors(idleColor, idleTextColor);
        }

        public void ShowFree()
        {
            label.text = "FREE";
            label.fontSize = freeFontSize;
            SetColors(claimedColor, claimedTextColor);
        }

        public void ShowDaubed() => SetColors(daubedColor, daubedTextColor);

        public void ShowClaimed() => SetColors(claimedColor, claimedTextColor);

        public void Flash()
        {
            _flashLeft = flashSeconds;
            enabled = true;
        }

        private void SetColors(Color background, Color text)
        {
            _baseColor = background;
            _background.color = background;
            label.color = text;
            _flashLeft = 0f;
            enabled = false;
            transform.localRotation = Quaternion.identity;
        }
    }
}
