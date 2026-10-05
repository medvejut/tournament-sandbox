using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TournamentSandbox.UI
{
    public sealed class MatchResultView : MonoBehaviour
    {
        [SerializeField] private TMP_Text titleLabel;
        [SerializeField] private TMP_Text scoreLabel;
        [SerializeField] private TMP_Text detailsLabel;
        [SerializeField] private TMP_Text noteLabel;
        [SerializeField] private TMP_Text seedLabel;
        [SerializeField] private Button playAgainButton;
        [SerializeField] private Button lobbyButton;

        public event Action PlayAgainClicked;
        public event Action LobbyClicked;

        private void Awake()
        {
            playAgainButton.onClick.AddListener(() => PlayAgainClicked?.Invoke());
            lobbyButton.onClick.AddListener(() => LobbyClicked?.Invoke());
        }

        public void Show(string title, int score, string details, string note, string seedLine)
        {
            titleLabel.text = title;
            scoreLabel.text = score.ToString();
            detailsLabel.text = details;
            noteLabel.text = note;
            seedLabel.text = seedLine;
        }

        public void ShowNote(string note) => noteLabel.text = note;
    }
}
