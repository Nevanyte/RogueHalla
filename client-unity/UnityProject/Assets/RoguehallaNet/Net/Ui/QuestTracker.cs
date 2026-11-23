using System.Collections.Generic;

namespace Roguehalla.Net.Ui
{
    /// <summary>One row of the quest tracker: an accepted quest that is still running.</summary>
    public readonly struct QuestTrackerRow
    {
        public readonly QuestRowView Quest;
        /// <summary>-1 without a deadline; 0 when it has passed.</summary>
        public readonly int SecondsLeft;
        public QuestTrackerRow(QuestRowView quest, int secondsLeft) { Quest = quest; SecondsLeft = secondsLeft; }
        /// <summary>True when both rows draw the same: the same quest object and the same seconds.</summary>
        public bool SameFace(QuestTrackerRow other) { return false; }
    }

    /// <summary>Which accepted quests the tracker lists, in which order, and the texts of a row.</summary>
    public static class QuestTracker
    {
        public const int MaxRows = 3, UrgentSeconds = 30, CriticalSeconds = 10, NameChars = 26, ObjectiveChars = 34;
        /// <summary>Clears rows, adds the first MaxRows quests to show, and returns how many accepted quests are running.
        /// A null or unavailable view gives 0.</summary>
        public static int Build(QuestView view, double serverMs, List<QuestTrackerRow> rows) { return 0; }
        public static string Clock(int secondsLeft) { return ""; }
        public static string Progress(QuestRowView quest) { return ""; }
        public static string Header(int running, int shown) { return ""; }
        public static int DwellSeconds(string def) { return 0; }
        /// <summary>text when it is at most maxChars long; else its first maxChars - 1 characters, trailing spaces
        /// removed, and "…". Null gives "".</summary>
        public static string Short(string text, int maxChars) { return ""; }
        /// <summary>How many rows fit between dockTop and dockFloor under the header: 0 to MaxRows.</summary>
        public static int RowsThatFit(float dockTop, float dockFloor) { return 0; }
    }
}
