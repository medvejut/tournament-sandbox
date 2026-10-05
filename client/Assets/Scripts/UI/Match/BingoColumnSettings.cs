using TournamentSandbox.Core;
using UnityEngine;

namespace TournamentSandbox.UI
{
    [CreateAssetMenu(menuName = "Tournament Sandbox/Bingo Column Settings")]
    public sealed class BingoColumnSettings : ScriptableObject
    {
        [Tooltip("One per column, B I N G O.")]
        public Color[] ballColors = new Color[Rules.Size];

        [Tooltip("Text on a ball of the same column; dark on a light ball.")]
        public Color[] ballTextColors = new Color[Rules.Size];

        public Color emptyBallColor = Color.gray;

        public static int ColumnOf(int number) => (number - 1) / Rules.NumbersPerColumn;
    }
}
