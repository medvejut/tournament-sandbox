using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TournamentSandbox.UI
{
    public sealed class LobbyView : MonoBehaviour
    {
        [SerializeField] private Button practiceButton;
        [SerializeField] private Button onlineButton;
        [SerializeField] private TMP_Text onlineStatusLabel;
        [SerializeField] private TMP_Text liveOpsBanner;
        [SerializeField] private TMP_Text balanceLabel;

        public event Action PracticeClicked;
        public event Action OnlineClicked;

        private void Awake()
        {
            practiceButton.onClick.AddListener(() => PracticeClicked?.Invoke());
            onlineButton.onClick.AddListener(() => OnlineClicked?.Invoke());
            ShowLiveOps(null);
            ShowBalance(null);
        }

        public void ShowOnlineStatus(string text) => onlineStatusLabel.text = text;

        public void ShowBalance(int? balance) => balanceLabel.text = balance.HasValue ? $"balance {balance}" : "";

        // Null hides the banner.
        public void ShowLiveOps(string text)
        {
            liveOpsBanner.gameObject.SetActive(text != null);
            if (text != null)
            {
                liveOpsBanner.text = text;
            }
        }

        public void SetBusy(bool busy)
        {
            practiceButton.interactable = !busy;
            onlineButton.interactable = !busy;
        }
    }
}
