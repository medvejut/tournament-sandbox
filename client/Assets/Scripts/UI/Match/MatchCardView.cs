using System;
using TournamentSandbox.Core;
using UnityEngine;

namespace TournamentSandbox.UI
{
    // Called numbers are deliberately not highlighted: spotting them is the skill being scored.
    public sealed class MatchCardView : MonoBehaviour
    {
        [Tooltip("All 25 cells, row-major: index = row * 5 + column.")]
        [SerializeField] private MatchCellView[] cells;

        public event Action<int> CellTapped;

        private void Awake()
        {
            for (var i = 0; i < cells.Length; i++)
            {
                var cell = i;
                cells[i].Tapped += () => CellTapped?.Invoke(cell);
            }
        }

        public void Bind(BingoGame game)
        {
            for (var cell = 0; cell < Rules.CellCount; cell++)
            {
                if (cell == Rules.FreeCell)
                {
                    cells[cell].ShowFree();
                }
                else
                {
                    cells[cell].ShowNumber(game.Card[cell]);
                }
            }
        }

        public void ShowDaubed(int cell) => cells[cell].ShowDaubed();

        public void ShowClaimed(int cell) => cells[cell].ShowClaimed();

        public void Flash(int cell) => cells[cell].Flash();
    }
}
