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
        public bool SameFace(QuestTrackerRow other) { return ReferenceEquals(Quest, other.Quest) && SecondsLeft == other.SecondsLeft; }
    }

    /// <summary>Which accepted quests the tracker lists, in which order, and the texts of a row.</summary>
    public static class QuestTracker
    {
        public const int MaxRows = 3, UrgentSeconds = 30, CriticalSeconds = 10, NameChars = 26, ObjectiveChars = 34;
        /// <summary>Clears rows, adds the first MaxRows quests to show, and returns how many accepted quests are running.
        /// A null or unavailable view gives 0.</summary>
        public static int Build(QuestView view, double serverMs, List<QuestTrackerRow> rows)
        {
            rows.Clear();
            if (view == null || !view.Available) return 0;
            int running = 0;
            var all = view.Rows;
            for (int i = 0; i < all.Count; i++)
            {
                var q = all[i];
                if (!q.OwnerAccepted || q.OwnerCompleted || q.State != Roguehalla.Net.Protocol.QuestRowState.Open) continue;
                running++;
                // insert after every row that sorts at or before it (stable); keep only the first MaxRows
                int at = rows.Count;
                while (at > 0 && Earlier(q, rows[at - 1].Quest)) at--;
                if (at >= MaxRows) continue;
                rows.Insert(at, new QuestTrackerRow(q, SecondsLeft(q, serverMs)));
                if (rows.Count > MaxRows) rows.RemoveAt(rows.Count - 1);
            }
            return running;
        }

        static int Tier(QuestRowView q) { return q.OwnerReady ? 0 : q.ExpiresAt > 0 ? 1 : 2; }

        // True when a sorts strictly before b.
        static bool Earlier(QuestRowView a, QuestRowView b)
        {
            int ta = Tier(a), tb = Tier(b);
            if (ta != tb) return ta < tb;
            if (ta == 2) return false;
            double ea = a.ExpiresAt > 0 ? a.ExpiresAt : double.MaxValue, eb = b.ExpiresAt > 0 ? b.ExpiresAt : double.MaxValue;
            return ea < eb;
        }

        static int SecondsLeft(QuestRowView q, double serverMs)
        {
            if (!(q.ExpiresAt > 0)) return -1;
            double s = System.Math.Ceiling((q.ExpiresAt - serverMs) / 1000.0);
            if (double.IsNaN(s) || double.IsInfinity(s) || s < 0) return 0;
            return s > 1000000000.0 ? 1000000000 : (int)s;
        }
        public static string Clock(int secondsLeft)
        {
            if (secondsLeft < 0) return "";
            int m = secondsLeft / 60, s = secondsLeft % 60;
            return m.ToString() + (s < 10 ? ":0" : ":") + s.ToString();
        }
        public static string Progress(QuestRowView quest)
        {
            if (quest == null) return "";
            if (quest.OwnerReady) return "Turn in";
            int dwell = DwellSeconds(quest.Def);
            if (dwell > 0) return quest.OwnerProgress.ToString() + " / " + dwell.ToString() + " s";
            return quest.OwnerProgress > 0 ? quest.OwnerProgress.ToString() : "";
        }
        public static string Header(int running, int shown)
        {
            return running > shown ? "+" + (running - shown).ToString() + " more · J" : "J · log";
        }
        public static int DwellSeconds(string def)
        {
            return def == "hold_the_fort" ? 45 : def == "stay_a_while" ? 30 : 0;
        }
        /// <summary>text when it is at most maxChars long; else its first maxChars - 1 characters, trailing spaces
        /// removed, and "…". Null gives "".</summary>
        public static string Short(string text, int maxChars)
        {
            if (text == null) return "";
            if (text.Length <= maxChars) return text;
            if (maxChars < 1) return "";
            return text.Substring(0, maxChars - 1).TrimEnd(' ') + "…";
        }
        /// <summary>How many rows fit between dockTop and dockFloor under the header: 0 to MaxRows.</summary>
        public static int RowsThatFit(float dockTop, float dockFloor)
        {
            float n = (float)System.Math.Floor((dockFloor - dockTop - HudRegions.TrackerHeader) / HudRegions.TrackerRow);
            if (float.IsNaN(n)) return 0;
            return n < 0f ? 0 : n > MaxRows ? MaxRows : (int)n;
        }
    }
}
