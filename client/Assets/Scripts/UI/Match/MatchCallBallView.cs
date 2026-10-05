using JetBrains.Annotations;
using TournamentSandbox.Game;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TournamentSandbox.UI
{
    [RequireComponent(typeof(Image))]
    public sealed class MatchCallBallView : MonoBehaviour
    {
        [SerializeField] private BingoColumnSettings columns;

        [Tooltip("Left empty, the letter is shown in front of the number instead (\"B7\").")]
        [CanBeNull]
        [SerializeField] private TMP_Text letterLabel;

        [SerializeField] private TMP_Text numberLabel;

        [Tooltip("Text colour comes from the column settings; off for a ball with its own label backdrop.")]
        [SerializeField] private bool tintText = true;

        [SerializeField] private string emptyText = "";

        private Image _image;

        private void Awake()
        {
            _image = GetComponent<Image>();
        }

        // 0 shows an empty ball.
        public void Show(int number)
        {
            if (number == 0)
            {
                _image.color = columns.emptyBallColor;
                numberLabel.text = emptyText;
                if (letterLabel != null)
                {
                    letterLabel.text = "";
                }
                return;
            }

            var column = BingoColumnSettings.ColumnOf(number);
            var letter = CallScheduler.ColumnLetter(number);
            _image.color = columns.ballColors[column];
            if (letterLabel != null)
            {
                letterLabel.text = letter.ToString();
                numberLabel.text = number.ToString();
            }
            else
            {
                numberLabel.text = $"{letter}{number}";
            }
            if (tintText)
            {
                numberLabel.color = columns.ballTextColors[column];
            }
        }
    }
}
