namespace Roguehalla.Net.Ui
{
    /// <summary>Which quest offer the player can take where they stand.</summary>
    public static class QuestOffer
    {
        /// <summary>The CanAccept row whose giver is nearest (x, y) by squared distance: the first met, replaced
        /// only by a strictly nearer one (ties and NaN keep the first). Null when none; inReach counts them.</summary>
        public static QuestRowView Pick(QuestView view, double x, double y, out int inReach)
        {
            inReach = 0;
            return null;
        }

        /// <summary>The row of this handle; null when absent, or the view is null or unavailable.</summary>
        public static QuestRowView Find(QuestView view, QuestHandle handle)
        {
            return null;
        }

        /// <summary>The card's top so that it ends at or above floor: regionTop if regionTop + regionHeight &lt;= floor,
        /// else floor - regionHeight, never above clear (the tracker's bottom). Any NaN gives regionTop.</summary>
        public static float Lift(float regionTop, float regionHeight, float clear, float floor)
        {
            return regionTop;
        }
    }
}
