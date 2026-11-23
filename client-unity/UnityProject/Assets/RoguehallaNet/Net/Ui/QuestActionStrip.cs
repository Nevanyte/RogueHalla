namespace Roguehalla.Net.Ui
{
    /// <summary>Sizes of the quest journal's strip of the selected quest's own actions: a window of at most two rows onto a longer list.</summary>
    public static class QuestActionStrip
    {
        public const float RowHeight = 44f, RowGap = 5f, ScrollGap = 6f;
        public const int MaxRows = 2;

        public static float Height(int count) { return 0f; }
        public static float ContentHeight(int count) { return 0f; }
        public static float ScrollBottom(int count) { return 0f; }
    }
}
