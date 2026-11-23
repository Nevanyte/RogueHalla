namespace Roguehalla.Net.Ui
{
    /// <summary>The journal's words for a quest: the state on its list row, the status line, the reason it cannot be
    /// accepted. Every input is a delivered fact.</summary>
    public static class QuestJournalCopy
    {
        /// <summary>The server's accept range in world units: used only to choose the hint's wording.</summary>
        public const double GiverReach = 64d;

        public static string RowState(QuestRowView q) { return ""; }

        public static string Status(QuestRowView q, bool acceptPending, bool rewardPending, int secondsLeft) { return ""; }

        public static string Hint(QuestRowView q, double distanceToGiver) { return ""; }

        public static bool ShowsBrief(QuestRowView q) { return true; }
    }
}
